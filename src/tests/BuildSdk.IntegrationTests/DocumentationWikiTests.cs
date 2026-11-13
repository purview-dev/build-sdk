using System.Text;
using System.Text.RegularExpressions;

namespace Purview.BuildSdk;

/// <summary>
/// Guards the shape of the documentation set in <c>docs/wiki</c>: every page sits at the root or in one topic
/// folder, is listed once in <c>_Sidebar.md</c>, is reachable from <c>Home.md</c>, and its links resolve.
/// </summary>
/// <remarks>
/// <para>
/// The folder tree is load-bearing, not decorative: purview.dev publishes each page at
/// <c>/docs/build-sdk/&lt;folder&gt;/&lt;page&gt;/</c> and resolves every relative link against the page that
/// contains it, so a page's location is part of its URL and a link is only correct relative to the file it is
/// written in. These tests read the tree the way the sync does - paths relative to <c>docs/wiki</c>, links
/// resolved from the containing file - which is why they catch a page that exists but is published nowhere
/// discoverable.
/// </para>
/// <para>
/// The rules are the published <c>purview-buildsdk-docs-sync</c> skill's; every assertion names the offending
/// file, because the failure mode is silence: the site publishes whatever it finds, in whatever order it finds it.
/// </para>
/// </remarks>
sealed partial class DocumentationWikiTests
{
	const string SidebarFile = "_Sidebar.md";
	const string HomeFile = "Home.md";
	const string LandingPage = "Getting-Started.md";

	/// <summary>
	/// The pages that must stay at the wiki root: the site's landing page (<c>docs.rootPage</c>) and the two pages
	/// its project catalogue deep-links through <c>docsPage</c> (<c>configuration-reference</c>,
	/// <c>repository-bootstrap</c>). A deep link is a single path segment, so those pages keep root-level URLs.
	/// </summary>
	static readonly string[] RootPages =
	[
		"Configuration-Reference.md",
		"Getting-Started.md",
		"Repository-Bootstrap.md",
	];

	/// <summary>The topic folders, one per <c>Home.md</c> section that is not at the root.</summary>
	static readonly string[] TopicFolders =
	[
		"analyzers",
		"concepts",
		"configuration",
		"development",
		"packaging",
		"testing",
		"tooling",
	];

	/// <summary>Repository documents outside the wiki whose links into it must resolve.</summary>
	static readonly string[] RepositoryDocuments = ["README.md", "AGENTS.md"];

	static readonly Lazy<string> RepositoryRoot = new(FindRepositoryRoot);

	static string WikiFolder => Path.Combine(RepositoryRoot.Value, "docs", "wiki");

	[Test]
	public async Task WikiFolder_ResolvesToThisRepository()
	{
		var indexFiles = new[] { SidebarFile, HomeFile, LandingPage }
			.Where(name => File.Exists(Path.Combine(WikiFolder, name)))
			.ToArray();

		// Without the marker walk landing here, every other test in this class passes vacuously against files
		// that were never found.
		await Assert
			.That(indexFiles)
			.IsEquivalentTo([SidebarFile, HomeFile, LandingPage])
			.Because($"'{WikiFolder}' does not look like this repository's docs/wiki folder");

		await Assert.That(PageFiles()).IsNotEmpty();
	}

	[Test]
	public async Task Pages_LiveAtTheRootOrInOneTopicFolder()
	{
		var pages = PageFiles();

		// A page's folder is the first segment of its published URL, so a second level of nesting would make the
		// URL say more about the wiki's filing than about the page.
		var tooDeep = pages.Where(path => path.Split('/').Length > 2).ToArray();
		var folders = pages
			.Select(path => path.Split('/'))
			.Where(segments => segments.Length > 1)
			.Select(segments => segments[0])
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();

		await Assert
			.That(tooDeep)
			.IsEmpty()
			.Because("every page belongs at the wiki root or in exactly one folder: " + string.Join(", ", tooDeep));

		await Assert
			.That(folders)
			.IsEquivalentTo(TopicFolders)
			.Because(
				"a new topic folder needs a sidebar section, a Home.md heading and an entry here; found: "
					+ string.Join(", ", folders)
			);
	}

	[Test]
	public async Task RootPages_AreExactlyTheSiteContractPages()
	{
		var rootPages = PageFiles().Where(path => !path.Contains('/', StringComparison.Ordinal)).ToArray();

		var unexpected = rootPages.Except(RootPages, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
		var missing = RootPages.Except(rootPages, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

		// Moving a deep-linked page changes a published URL that the site catalogue - another repository - points at.
		await Assert
			.That(unexpected)
			.IsEmpty()
			.Because(
				"a page at the wiki root needs a site contract behind it (docs.rootPage or a docsPage deep link); "
					+ "otherwise it belongs in a topic folder: "
					+ string.Join(", ", unexpected)
			);

		await Assert
			.That(missing)
			.IsEmpty()
			.Because("these pages must stay at the wiki root: " + string.Join(", ", missing));
	}

	[Test]
	public async Task Pages_UseUrlSafeNames()
	{
		// A page's folder and file name become its URL, lowercased: `packaging/SourceLink.md` is published at
		// /docs/build-sdk/packaging/sourcelink/. A space, a dot or a non-ASCII character would be rewritten by the
		// sync, so the address would stop mirroring the tree.
		var unsafeNames = PageFiles()
			.SelectMany(path => path.Split('/'))
			.Select(segment => segment.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? segment[..^3] : segment)
			.Where(segment => !UrlSafeNamePattern().IsMatch(segment))
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();

		await Assert
			.That(unsafeNames)
			.IsEmpty()
			.Because(
				"folders and page names become URL segments verbatim, so they must be letters, digits and dashes: "
					+ string.Join(", ", unsafeNames)
			);
	}

	[Test]
	public async Task Sidebar_ListsEveryPageExactlyOnce(CancellationToken cancellationToken)
	{
		var sidebar = await ReadWikiFileAsync(SidebarFile, cancellationToken);
		var listed = PageTargets(sidebar, SidebarFile);
		var pages = PageFiles();

		var unlisted = pages.Except(listed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
		var duplicated = listed
			.GroupBy(path => path, StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.Select(group => $"{group.Key} (listed {group.Count()} times)")
			.Order(StringComparer.Ordinal)
			.ToArray();

		// The sidebar is the page-order authority for the published site, so an unlisted page is published last,
		// and a duplicated one has two competing positions.
		await Assert
			.That(unlisted)
			.IsEmpty()
			.Because(
				$"every page must appear in {SidebarFile}, inside its section's list, by its path from docs/wiki: "
					+ string.Join(", ", unlisted)
			);

		await Assert
			.That(duplicated)
			.IsEmpty()
			.Because($"a page may appear in {SidebarFile} only once: " + string.Join(", ", duplicated));
	}

	[Test]
	public async Task Home_LinksEveryPage(CancellationToken cancellationToken)
	{
		var home = await ReadWikiFileAsync(HomeFile, cancellationToken);
		var linked = PageTargets(home, HomeFile);

		// Home.md is the index a reader browses the folders with, so a page missing from it is invisible to anyone
		// who did not already know its name. Only reachability is asserted; exactly-once ordering is the sidebar's.
		var unlisted = PageFiles().Except(linked, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

		await Assert
			.That(unlisted)
			.IsEmpty()
			.Because($"every page must appear in {HomeFile}: " + string.Join(", ", unlisted));
	}

	[Test]
	public async Task Home_HasAHeadingForEverySidebarSection(CancellationToken cancellationToken)
	{
		var sidebar = StripCode(await ReadWikiFileAsync(SidebarFile, cancellationToken));
		var home = StripCode(await ReadWikiFileAsync(HomeFile, cancellationToken));

		// The labels are what the sidebar shows and the headings are what Home.md shows; a section renamed in one
		// place only leaves the two disagreeing about what the wiki contains.
		var labels = SectionLabelPattern().Matches(sidebar).Select(match => match.Groups[1].Value.Trim()).ToArray();
		var headings = Heading2Pattern().Matches(home).Select(match => match.Groups[1].Value.Trim()).ToArray();
		var unmatched = labels.Except(headings, StringComparer.Ordinal).ToArray();

		await Assert.That(labels).IsNotEmpty();
		await Assert
			.That(unmatched)
			.IsEmpty()
			.Because(
				$"every '{SidebarFile}' section needs a matching '## ' heading in {HomeFile}: "
					+ string.Join(", ", unmatched)
			);
	}

	[Test]
	public async Task WikiLinks_ResolveToExistingPages(CancellationToken cancellationToken)
	{
		List<string> broken = [];

		foreach (var file in PageFiles().Append(SidebarFile).Append(HomeFile))
		{
			var markdown = StripCode(await ReadWikiFileAsync(file, cancellationToken));
			foreach (var target in MarkdownLinkTargets(markdown))
			{
				var path = StripAnchor(target);
				if (!IsRelativeMarkdownLink(path))
					continue;

				// A link that climbs out of docs/wiki is a repository path, which the site rewrites to a GitHub URL;
				// it must still resolve in the repository.
				var resolved = Path.GetFullPath(
					Path.Combine(Path.GetDirectoryName(Path.Combine(WikiFolder, file))!, path)
				);
				if (!File.Exists(resolved))
					broken.Add($"{file} -> {target}");
			}
		}

		// A link is resolved against the page it appears on, so a page that moved without its incoming links being
		// updated breaks only for the reader who follows it.
		await Assert
			.That(broken)
			.IsEmpty()
			.Because("these links point at files that do not exist: " + string.Join(", ", broken));
	}

	[Test]
	public async Task RepositoryDocuments_LinksIntoTheWikiResolve(CancellationToken cancellationToken)
	{
		List<string> broken = [];

		foreach (var document in RepositoryDocuments)
		{
			var markdown = StripCode(
				(
					await File.ReadAllTextAsync(Path.Combine(RepositoryRoot.Value, document), cancellationToken)
				).ReplaceLineEndings("\n")
			);

			foreach (var target in MarkdownLinkTargets(markdown))
			{
				var path = StripAnchor(target);
				if (!IsRelativeMarkdownLink(path) || !path.Contains("docs/wiki/", StringComparison.Ordinal))
					continue;

				if (!File.Exists(Path.Combine(RepositoryRoot.Value, path.Replace('/', Path.DirectorySeparatorChar))))
					broken.Add($"{document} -> {target}");
			}
		}

		// Moving a wiki page into a topic folder leaves every README and AGENTS.md link to its old path pointing at
		// nothing, and GitHub renders a broken link without complaint.
		await Assert
			.That(broken)
			.IsEmpty()
			.Because("these links into docs/wiki point at pages that do not exist: " + string.Join(", ", broken));
	}

	[Test]
	public async Task Pages_OpenWithASingleH1Title(CancellationToken cancellationToken)
	{
		List<string> problems = [];

		foreach (var page in PageFiles())
		{
			var markdown = StripCode(await ReadWikiFileAsync(page, cancellationToken));
			var lines = markdown.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
			var titles = lines.Count(line => Heading1Pattern().IsMatch(line));

			if (titles != 1)
				problems.Add($"{page} has {titles} '# ' headings");
			else if (!Heading1Pattern().IsMatch(lines[0]))
				problems.Add($"{page} does not open with its '# ' heading");
		}

		// The first heading is the title the published sidebar shows, so a page without exactly one is either
		// untitled or titled with whichever section heading happens to come first.
		await Assert.That(problems).IsEmpty().Because(string.Join("; ", problems));
	}

	/// <summary>Every page in the wiki, as a path relative to <c>docs/wiki</c> with <c>/</c> separators.</summary>
	static string[] PageFiles() =>
		[
			.. Directory
				.GetFiles(WikiFolder, "*.md", SearchOption.AllDirectories)
				.Select(path => Path.GetRelativePath(WikiFolder, path).Replace('\\', '/'))
				.Where(path => path is not SidebarFile and not HomeFile)
				.Order(StringComparer.Ordinal),
		];

	/// <summary>
	/// The pages a document links to, as paths relative to <c>docs/wiki</c>, resolved against the document's own
	/// folder - which is what both the site and GitHub do. A link that climbs above the wiki folder is dropped.
	/// </summary>
	static string[] PageTargets(string markdown, string fromPath) =>
		[
			.. MarkdownLinkTargets(StripCode(markdown))
				.Select(StripAnchor)
				.Where(IsRelativeMarkdownLink)
				.Select(target => ResolveWithinWiki(fromPath, target))
				.OfType<string>(),
		];

	/// <summary>Resolves a link the way a browser does, or null when it points outside the wiki folder.</summary>
	static string? ResolveWithinWiki(string fromPath, string target)
	{
		List<string> segments = [.. fromPath.Split('/')[..^1]];

		foreach (var segment in target.Split('/'))
		{
			if (segment is "" or ".")
				continue;

			if (segment == "..")
			{
				if (segments.Count == 0)
					return null;

				segments.RemoveAt(segments.Count - 1);
				continue;
			}

			segments.Add(segment);
		}

		return segments.Count == 0 ? null : string.Join('/', segments);
	}

	static async Task<string> ReadWikiFileAsync(string path, CancellationToken cancellationToken) =>
		(
			await File.ReadAllTextAsync(
				Path.Combine(WikiFolder, path.Replace('/', Path.DirectorySeparatorChar)),
				cancellationToken
			)
		)
		// Line endings are normalised so a pattern does not have to tolerate CRLF; the files are checked out with
		// whatever the machine's autocrlf setting produces.
		.ReplaceLineEndings("\n");

	/// <summary>
	/// Removes fenced blocks and inline code spans. Neither is markdown structure - a quoted <c># comment</c> or a
	/// quoted link is an example - so reading them would report problems the reader never sees.
	/// </summary>
	static string StripCode(string markdown)
	{
		StringBuilder builder = new(markdown.Length);
		var inFence = false;

		foreach (var line in markdown.Split('\n'))
		{
			if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
			{
				inFence = !inFence;
				continue;
			}

			if (!inFence)
				builder.Append(InlineCodePattern().Replace(line, string.Empty)).Append('\n');
		}

		return builder.ToString();
	}

	static string StripAnchor(string target) => target.Split('#', 2)[0].Split('?', 2)[0];

	static string[] MarkdownLinkTargets(string markdown) =>
		[.. LinkPattern().Matches(markdown).Select(match => match.Groups[1].Value.Trim())];

	/// <summary>Whether a link target names a markdown file: relative, and not an external URL.</summary>
	static bool IsRelativeMarkdownLink(string path) =>
		path.Length > 0
		&& path is not ['/', ..]
		&& !path.Contains("://", StringComparison.Ordinal)
		&& path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

	static string FindRepositoryRoot()
	{
		for (
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			directory is not null;
			directory = directory.Parent
		)
		{
			var wiki = Path.Combine(directory.FullName, "docs", "wiki");
			if (
				File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
				&& File.Exists(Path.Combine(wiki, SidebarFile))
				&& File.Exists(Path.Combine(wiki, HomeFile))
			)
			{
				return directory.FullName;
			}
		}

		throw new InvalidOperationException(
			$"No 'docs/wiki' carrying {SidebarFile} and {HomeFile} above '{AppContext.BaseDirectory}'."
		);
	}

	[GeneratedRegex(@"!?\[[^\]]*\]\(([^)\s]+)\)")]
	private static partial Regex LinkPattern();

	[GeneratedRegex(@"`[^`\n]*`")]
	private static partial Regex InlineCodePattern();

	[GeneratedRegex(@"^#\s", RegexOptions.Multiline)]
	private static partial Regex Heading1Pattern();

	[GeneratedRegex(@"^##\s+(.+?)\s*$", RegexOptions.Multiline)]
	private static partial Regex Heading2Pattern();

	[GeneratedRegex(@"^[ \t]*-[ \t]+\*\*(.+?)\*\*[ \t]*$", RegexOptions.Multiline)]
	private static partial Regex SectionLabelPattern();

	[GeneratedRegex(@"^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$")]
	private static partial Regex UrlSafeNamePattern();
}
