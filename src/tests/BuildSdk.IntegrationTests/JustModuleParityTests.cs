using System.Text.RegularExpressions;

namespace Purview.BuildSdk;

/// <summary>
/// Holds this repository's <c>.build/just/</c> modules to the published
/// <c>purview-buildsdk-justfile</c> skill's <c>assets/just/</c>, which they are copied from.
/// </summary>
/// <remarks>
/// <para>
/// The modules are written once, in the skill, and copied verbatim into every repository that uses
/// them - this one included. An edit made only to the <c>.build/just/</c> copy would never reach a
/// consumer, and the next copy would silently undo it; an edit made only to the skill would ship
/// recipes this repository never ran. Comparing the two is the only thing that notices either.
/// </para>
/// <para>
/// The root <c>Justfile</c> sets <c>allow-duplicate-recipes</c>, so a recipe it defines with a
/// module's name replaces the module's without a word. That is how a hook is overridden, and also how
/// a stale copy of a migrated recipe would keep the old behaviour, so only <c>_</c>-prefixed hooks
/// may share a name.
/// </para>
/// </remarks>
sealed partial class JustModuleParityTests
{
	/// <summary>A recipe header at column 0: <c>name params: deps</c>, not an assignment.</summary>
	[GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_-]*)(?:\s[^:\n]*)?:(?!=)", RegexOptions.Multiline)]
	private static partial Regex RecipeHeaderPattern();

	/// <summary>An unconditional import in the barrel - <c>import "x.just"</c>, not <c>import?</c>.</summary>
	[GeneratedRegex(@"^import\s+""(?<path>[^""]+)""", RegexOptions.Multiline)]
	private static partial Regex RequiredImportPattern();

	/// <summary>Any import, optional or not.</summary>
	[GeneratedRegex(@"^import\??\s+""(?<path>[^""]+)""", RegexOptions.Multiline)]
	private static partial Regex AnyImportPattern();

	static readonly string[] NotRecipes = ["set", "import", "export", "mod", "alias"];

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
				$"No AGENTS.md was found above '{AppContext.BaseDirectory}', so the repository root could not be resolved."
			);
		}
	}

	static string ModuleFolder => Path.Combine(RepositoryRoot, ".build", "just");

	static string AssetFolder => Path.Combine(RepositoryRoot, "skills", "purview-buildsdk-justfile", "assets", "just");

	/// <summary>Reads a file with line endings normalised, so a CRLF checkout compares equal.</summary>
	static string ReadNormalised(string path) => File.ReadAllText(path).ReplaceLineEndings("\n");

	static string[] FileNames(string folder) =>
		[.. Directory.GetFiles(folder).Select(Path.GetFileName).Select(name => name!).Order(StringComparer.Ordinal)];

	static string[] RecipeNames(string justfileText) =>
		[
			.. RecipeHeaderPattern()
				.Matches(justfileText)
				.Select(match => match.Groups["name"].Value)
				.Where(name => !NotRecipes.Contains(name, StringComparer.Ordinal))
				.Distinct(StringComparer.Ordinal),
		];

	[Test]
	public async Task EveryModule_MatchesTheSkillAsset()
	{
		var modules = FileNames(ModuleFolder);
		await Assert.That(modules).IsNotEmpty();

		var assets = FileNames(AssetFolder);
		var unpublished = modules.Except(assets, StringComparer.Ordinal).ToArray();
		await Assert
			.That(unpublished)
			.IsEmpty()
			.Because(
				".build/just/ holds files the purview-buildsdk-justfile skill does not ship - add them to its assets/just/, "
					+ "or keep repository-only recipes in the root Justfile: "
					+ string.Join(", ", unpublished)
			);

		foreach (var module in modules)
		{
			var copy = ReadNormalised(Path.Combine(ModuleFolder, module));
			var asset = ReadNormalised(Path.Combine(AssetFolder, module));

			await Assert
				.That(copy)
				.IsEqualTo(asset)
				.Because(
					$".build/just/{module} differs from skills/purview-buildsdk-justfile/assets/just/{module}. "
						+ "Change the skill's copy and copy it over, never the other way round."
				);
		}
	}

	[Test]
	public async Task EveryRequiredModule_IsPresent()
	{
		// The barrel's unconditional imports fail every recipe when a file is missing, so a repository
		// may leave out only the import? modules.
		var barrel = ReadNormalised(Path.Combine(ModuleFolder, "index.just"));
		var required = RequiredImportPattern().Matches(barrel).Select(match => match.Groups["path"].Value).ToArray();

		await Assert.That(required).IsNotEmpty();

		foreach (var module in required)
		{
			await Assert
				.That(File.Exists(Path.Combine(ModuleFolder, module)))
				.IsTrue()
				.Because($"index.just imports '{module}' unconditionally");
		}
	}

	[Test]
	public async Task RootJustfile_ImportsOnlyTheBarrel()
	{
		var root = ReadNormalised(Path.Combine(RepositoryRoot, "Justfile"));
		var imports = AnyImportPattern().Matches(root).Select(match => match.Groups["path"].Value).ToArray();

		await Assert.That(imports).IsEquivalentTo([".build/just/index.just"]);
	}

	[Test]
	public async Task RootJustfile_DoesNotRedefineAModuleRecipe()
	{
		var moduleRecipes = FileNames(ModuleFolder)
			.Where(name => name.EndsWith(".just", StringComparison.Ordinal))
			.SelectMany(name => RecipeNames(ReadNormalised(Path.Combine(ModuleFolder, name))))
			.ToHashSet(StringComparer.Ordinal);

		await Assert.That(moduleRecipes).Contains("build");

		var redefined = RecipeNames(ReadNormalised(Path.Combine(RepositoryRoot, "Justfile")))
			.Where(name => moduleRecipes.Contains(name) && name is not ['_', ..])
			.ToArray();

		await Assert
			.That(redefined)
			.IsEmpty()
			.Because(
				"allow-duplicate-recipes makes a root recipe silently replace the module's; only _-prefixed hooks "
					+ "may be overridden: "
					+ string.Join(", ", redefined)
			);
	}
}
