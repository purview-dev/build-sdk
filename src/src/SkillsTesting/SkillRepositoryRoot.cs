namespace Purview.Skills.Testing;

/// <summary>
/// Finds the repository root by walking up from a starting directory looking for marker files and
/// folders.
/// </summary>
/// <remarks>
/// <para>
/// This is a managed port of the SDK's <c>FindPurviewRepositoryRoot</c> inline MSBuild task, and the
/// two are kept in step deliberately: skills belong to the repository rather than to a project, so a
/// test that validates them has to agree with the build about which directory that is. A test that
/// resolved a different root from <c>InstallPurviewSkills</c> would validate one set of skills while
/// the build installed another.
/// </para>
/// <para>
/// Three properties of the walk are load-bearing and easy to lose in a rewrite:
/// </para>
/// <list type="bullet">
/// <item>
/// The first marker is <b>definitive</b> and is searched all the way up, so <c>.git/</c> wins over a
/// nearer marker and a monorepo sub-package cannot claim to be the repository.
/// </item>
/// <item>
/// Every other marker resolves <b>nearest-ancestor-first</b>, with list order breaking ties inside a
/// single directory. Searching them all to the top would let a stray <c>.claude/</c> in a developer's
/// home directory outrank the real root.
/// </item>
/// <item>
/// The home directory and everything above it are <b>never candidates</b>. Without that bound, a
/// project outside any repository resolves to <c>~</c>. A repository below the home directory is
/// unaffected, because it is below it.
/// </item>
/// </list>
/// </remarks>
public static class SkillRepositoryRoot
{
	/// <summary>
	/// The default markers, most authoritative first, matching the SDK's
	/// <c>PurviewRepositoryRootMarkers</c> default.
	/// </summary>
	/// <remarks>
	/// A trailing slash marks a directory. A directory marker still matches a <i>file</i> of that
	/// name, because <c>.git</c> is a directory in a clone but a file in a worktree or a submodule,
	/// and both identify a repository root.
	/// </remarks>
	public static IReadOnlyList<string> DefaultMarkers { get; } =
	[
		".git/",
		".github/",
		".gitattributes",
		"LICENSE.md",
		"AGENTS.md",
		".agents/",
		"CLAUDE.md",
		".claude/",
		"package.json",
	];

	/// <summary>Finds the repository root above the test assembly's location.</summary>
	/// <returns>The repository root, or <see langword="null"/> when no marker was found.</returns>
	public static string? Find() => Find(AppContext.BaseDirectory, DefaultMarkers);

	/// <summary>Finds the repository root above a starting directory.</summary>
	/// <param name="startDirectory">The directory to start from.</param>
	/// <param name="markers">The markers, most authoritative first. The first is definitive.</param>
	/// <returns>The repository root, or <see langword="null"/> when no marker was found.</returns>
	public static string? Find(string startDirectory, IReadOnlyList<string> markers) =>
		FindWithMarker(startDirectory, markers).Root;

	/// <summary>Finds the repository root, reporting which marker identified it.</summary>
	/// <param name="startDirectory">The directory to start from.</param>
	/// <param name="markers">The markers, most authoritative first. The first is definitive.</param>
	/// <returns>The root and the marker that matched, both <see langword="null"/> when none did.</returns>
	public static (string? Root, string? Marker) FindWithMarker(string startDirectory, IReadOnlyList<string> markers)
	{
		ArgumentNullException.ThrowIfNull(markers);

		if (string.IsNullOrEmpty(startDirectory) || !Directory.Exists(startDirectory))
			return (null, null);

		var ancestors = Ancestors(startDirectory);
		var parsed = markers
			.Select(marker => marker?.Trim() ?? string.Empty)
			.Where(marker => marker.TrimEnd('/', '\\').Length > 0)
			.Select(marker =>
				(
					Name: marker,
					Path: marker.TrimEnd('/', '\\'),
					IsDirectory: marker.EndsWith('/') || marker.EndsWith('\\')
				)
			)
			.ToArray();

		if (parsed.Length == 0)
			return (null, null);

		// Pass 1: the definitive marker, searched all the way up.
		foreach (var ancestor in ancestors)
		{
			if (Matches(ancestor, parsed[0].Path, parsed[0].IsDirectory))
				return (ancestor, parsed[0].Name);
		}

		// Pass 2: nearest ancestor holding any marker, list order breaking ties within one directory.
		foreach (var ancestor in ancestors)
		{
			foreach (var marker in parsed)
			{
				if (Matches(ancestor, marker.Path, marker.IsDirectory))
					return (ancestor, marker.Name);
			}
		}

		return (null, null);
	}

	/// <summary>Builds the ancestor chain, excluding the home directory and everything above it.</summary>
	/// <param name="startDirectory">The directory to start from.</param>
	/// <returns>The candidate directories, nearest first.</returns>
	static List<string> Ancestors(string startDirectory)
	{
		HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase);
		try
		{
			var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (!string.IsNullOrEmpty(home))
			{
				for (
					var directory = new DirectoryInfo(Path.GetFullPath(home));
					directory is not null;
					directory = directory.Parent
				)
				{
					excluded.Add(directory.FullName.TrimEnd('/', '\\'));
				}
			}
		}
		catch (ArgumentException) { }
		catch (PathTooLongException) { }

		List<string> ancestors = [];
		try
		{
			for (
				var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
				directory is not null;
				directory = directory.Parent
			)
			{
				if (!excluded.Contains(directory.FullName.TrimEnd('/', '\\')))
					ancestors.Add(directory.FullName);
			}
		}
		catch (ArgumentException) { }
		catch (PathTooLongException) { }
		catch (IOException) { }

		return ancestors;
	}

	/// <summary>Reports whether a directory holds a marker.</summary>
	/// <param name="directory">The directory to test.</param>
	/// <param name="marker">The marker name, without a trailing slash.</param>
	/// <param name="isDirectory">Whether the marker was declared as a directory.</param>
	/// <returns><see langword="true"/> when the marker is present.</returns>
	static bool Matches(string directory, string marker, bool isDirectory)
	{
		string candidate;
		try
		{
			candidate = Path.Combine(directory, marker);
		}
		catch (ArgumentException)
		{
			return false;
		}

		return isDirectory ? Directory.Exists(candidate) || File.Exists(candidate) : File.Exists(candidate);
	}
}
