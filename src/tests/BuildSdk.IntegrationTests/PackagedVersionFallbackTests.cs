using System.Text.Json;
using System.Text.RegularExpressions;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Pins the version literals that are packed into the SDK, or shown to someone adopting it, against the
/// single source of truth in <c>package.json</c>.
/// </summary>
/// <remarks>
/// These drift silently and are only noticed by a consumer. <c>PurviewBuildSdkVersionForGlobalJson</c> is
/// the one that bites: it is the version written into a bootstrapped <c>global.json</c> when the SDK
/// cannot detect its own version from the restored package path, so a stale fallback pins a brand-new
/// repository to a release that predates every fix in the SDK doing the bootstrapping.
/// </remarks>
public sealed partial class PackagedVersionFallbackTests
{
	[GeneratedRegex(
		@"<PurviewBuildSdkVersionForGlobalJson[^>]*>\s*(?<version>[^<\s]+)\s*</PurviewBuildSdkVersionForGlobalJson>",
		RegexOptions.Singleline
	)]
	private static partial Regex GlobalJsonVersionFallback();

	[GeneratedRegex(@"""Purview\.BuildSdk""\s*:\s*""(?<version>[^""]+)""")]
	private static partial Regex BuildSdkPin();

	[GeneratedRegex(@"^#\s*Purview\.BuildSdk\s+(?<version>\S+)")]
	private static partial Regex EditorConfigVersionMarker();

	[GeneratedRegex(
		@"<PurviewEditorConfigVersionMarker[^>]*>\s*(?<marker>.*?)</PurviewEditorConfigVersionMarker>",
		RegexOptions.Singleline
	)]
	private static partial Regex EditorConfigVersionMarkerRegex();

	static string RepositoryRoot
	{
		get
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory is not null)
			{
				if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
					return directory.FullName;

				directory = directory.Parent;
			}

			throw new InvalidOperationException(
				$"Could not locate the repository root above '{AppContext.BaseDirectory}'."
			);
		}
	}

	static string PackageVersion()
	{
		using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "package.json")));

		return document.RootElement.GetProperty("version").GetString()
			?? throw new InvalidOperationException("package.json has no 'version'.");
	}

	[Test]
	public async Task GlobalJsonVersionFallback_MatchesPackageJson(CancellationToken cancellationToken)
	{
		var defaults = await File.ReadAllTextAsync(
			Path.Combine(RepositoryRoot, "src", "src", "BuildSdk", "Sdk", "Props", "Defaults.props"),
			cancellationToken
		);

		var match = GlobalJsonVersionFallback().Match(defaults);

		await Assert.That(match.Success).IsTrue();
		await Assert.That(match.Groups["version"].Value).IsEqualTo(PackageVersion());
	}

	/// <summary>
	/// The marker has to be the first line of the repository's <c>.editorconfig</c>, because that is where
	/// the pack-time stamp rewrites it and where the SDK later looks for it.
	/// </summary>
	/// <remarks>
	/// The <em>version</em> in the tracked file is deliberately not asserted against <c>package.json</c>.
	/// Packing stamps the version in (see <c>StampPurviewEditorConfigVersionTarget</c>), so requiring the two
	/// to match by hand would reintroduce exactly the manual step the stamp removes — and a mismatch here
	/// has no effect on what ships. What matters is that the marker exists and is well formed; without it
	/// the stamp fails the build rather than packing an unrecognisable file.
	/// </remarks>
	[Test]
	public async Task EditorConfigVersionMarker_IsTheFirstLineAndWellFormed(CancellationToken cancellationToken)
	{
		var lines = await File.ReadAllLinesAsync(Path.Combine(RepositoryRoot, ".editorconfig"), cancellationToken);

		await Assert.That(lines).IsNotEmpty();

		var match = EditorConfigVersionMarker().Match(lines[0]);

		await Assert.That(match.Success).IsTrue();
		await Assert.That(Version.TryParse(match.Groups["version"].Value, out _)).IsTrue();
	}

	/// <summary>
	/// The marker the SDK matches on at build time has to be a prefix of the line it actually writes, or a
	/// managed file is never recognised as managed and the update silently never happens.
	/// </summary>
	[Test]
	public async Task EditorConfigVersionMarker_IsPrefixedByTheDefaultsPropsMarker(CancellationToken cancellationToken)
	{
		var defaults = await File.ReadAllTextAsync(
			Path.Combine(RepositoryRoot, "src", "src", "BuildSdk", "Sdk", "Props", "Defaults.props"),
			cancellationToken
		);

		var marker = EditorConfigVersionMarkerRegex()
			.Match(defaults)
			.Groups["marker"]
			.Value.Replace("&#32;", " ", StringComparison.Ordinal);

		await Assert.That(marker).IsNotEmpty();

		var firstLine = (
			await File.ReadAllLinesAsync(Path.Combine(RepositoryRoot, ".editorconfig"), cancellationToken)
		)[0];

		await Assert.That(firstLine.StartsWith(marker, StringComparison.Ordinal)).IsTrue();
	}

	/// <summary>
	/// Drives the real pack-time stamping target and asserts it writes the version it was packed with.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the mechanism that keeps a consumer's bootstrapped <c>.editorconfig</c> honest, so it is
	/// worth testing against the target itself rather than against its effect on one particular pack. The
	/// version asked for is deliberately not the repository's: a stamp that happens to agree with the
	/// tracked file proves nothing.
	/// </para>
	/// <para>
	/// The tracked <c>.editorconfig</c> must come back unchanged. A build that rewrites a tracked source
	/// file leaves a dirty working tree on CI and races when projects build in parallel.
	/// </para>
	/// </remarks>
	[Test]
	public async Task StampTarget_WritesThePackedVersionAndLeavesTheTrackedFileAlone(
		CancellationToken cancellationToken
	)
	{
		const string stampedVersion = "4.5.6-test";

		var projectPath = Path.Combine(RepositoryRoot, "src", "src", "BuildSdk", "BuildSdk.csproj");
		var trackedPath = Path.Combine(RepositoryRoot, ".editorconfig");
		var trackedBefore = await File.ReadAllTextAsync(trackedPath, cancellationToken);

		var scratchDirectory = Path.Combine(Path.GetTempPath(), "purview-stamp-" + Guid.NewGuid().ToString("N"));
		var destination = Path.Combine(scratchDirectory, ".editorconfig");

		try
		{
			var (exitCode, stdOut, stdErr) = await ProcessRunner.RunDotNetAsync(
				$"msbuild \"{projectPath}\" -t:StampPurviewEditorConfigVersionTarget -p:PackageVersion={stampedVersion} \"-p:_PurviewStampedEditorConfig={destination}\" -v:m",
				RepositoryRoot,
				cancellationToken
			);
			var output = stdOut + stdErr;

			await Assert.That(exitCode).IsEqualTo(0).Because(output);
			await Assert.That(File.Exists(destination)).IsTrue().Because(output);

			var stamped = await File.ReadAllLinesAsync(destination, cancellationToken);
			await Assert.That(stamped[0]).IsEqualTo($"# Purview.BuildSdk {stampedVersion} - managed file, do not edit");

			// Only the marker is rewritten; the rules themselves are packed verbatim.
			var trackedLines = await File.ReadAllLinesAsync(trackedPath, cancellationToken);
			await Assert.That(stamped.Length).IsEqualTo(trackedLines.Length);
			await Assert.That(stamped.Skip(1)).IsEquivalentTo(trackedLines.Skip(1));
		}
		finally
		{
			if (Directory.Exists(scratchDirectory))
				Directory.Delete(scratchDirectory, recursive: true);
		}

		await Assert.That(await File.ReadAllTextAsync(trackedPath, cancellationToken)).IsEqualTo(trackedBefore);
	}

	/// <summary>
	/// The quick-start snippets and the starter template are what somebody adopting the SDK copies, so a
	/// stale pin there is adopted verbatim.
	/// </summary>
	[Test]
	[Arguments("README.md")]
	[Arguments("docs/wiki/Getting-Started.md")]
	[Arguments("templates/global.json")]
	public async Task AdoptionPin_MatchesPackageJson(string relativePath, CancellationToken cancellationToken)
	{
		var path = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
		var content = await File.ReadAllTextAsync(path, cancellationToken);

		var pins = BuildSdkPin()
			.Matches(content)
			.Select(match => match.Groups["version"].Value)
			// The property reference in the auto-detection documentation is not a pin.
			.Where(version => !version.StartsWith('$'))
			.ToArray();

		await Assert.That(pins).IsNotEmpty();

		foreach (var pin in pins)
			await Assert.That(pin).IsEqualTo(PackageVersion());
	}
}
