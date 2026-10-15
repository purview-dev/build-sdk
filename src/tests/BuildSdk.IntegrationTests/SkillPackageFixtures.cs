namespace Purview.BuildSdk;

// CA1308 prefers ToUpperInvariant for normalisation, but these calls are not normalising for
// comparison: NuGet's global-packages layout is lowercase on disk and in the assets file's "path"
// value, so a fake restored package has to be written to the lowercase folder to be found at all.
#pragma warning disable CA1308

/// <summary>
/// Builds the on-disk shapes the skill targets read: a restored package that ships skills, the
/// <c>project.assets.json</c> entry that makes it part of the restore graph, and the skills CLI's
/// own <c>skills-lock.json</c>. Writing these directly keeps the tests offline and free of any
/// dependency on a published package or on Node tooling being present.
/// </summary>
static class SkillPackageFixtures
{
	public const string DefaultCommit = "0123456789abcdef0123456789abcdef01234567";

	/// <summary>
	/// Writes a fake restored package folder. The layout mirrors what
	/// <c>PurviewPackRepositorySkills</c> produces: skills under <c>.agents/skills/</c> and the git
	/// identity in the nuspec's <c>repository</c> element.
	/// </summary>
	public static void WritePackage(
		string packagesRoot,
		string packageId,
		string version,
		string[] skillNames,
		string? repositoryUrl = "https://github.com/purview-dev/purview-example/",
		string? repositoryCommit = DefaultCommit
	)
	{
		var packageRoot = Path.Combine(packagesRoot, packageId.ToLowerInvariant(), version);
		Directory.CreateDirectory(packageRoot);

		foreach (var skillName in skillNames)
		{
			var skillDirectory = Path.Combine(packageRoot, ".agents", "skills", skillName);
			Directory.CreateDirectory(skillDirectory);
			File.WriteAllText(
				Path.Combine(skillDirectory, "SKILL.md"),
				$"---\nname: {skillName}\ndescription: Use when exercising skill resolution.\n---\n"
			);
		}

		var repositoryElement =
			repositoryUrl is null ? string.Empty
			: repositoryCommit is null ? $"""<repository type="git" url="{repositoryUrl}" />"""
			: $"""<repository type="git" url="{repositoryUrl}" commit="{repositoryCommit}" />""";

		File.WriteAllText(
			Path.Combine(packageRoot, $"{packageId.ToLowerInvariant()}.nuspec"),
			$"""
			<?xml version="1.0" encoding="utf-8"?>
			<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
				<metadata>
					<id>{packageId}</id>
					<version>{version}</version>
					{repositoryElement}
				</metadata>
			</package>
			"""
		);
	}

	public static void WriteAssetsFile(string assetsPath, params (string Id, string Version)[] packages)
	{
		var libraries = string.Join(
			",\n",
			packages.Select(package =>
				$$"""
							"{{package.Id}}/{{package.Version}}": {
								"sha512": "",
								"type": "package",
								"path": "{{package.Id.ToLowerInvariant()}}/{{package.Version}}",
								"files": []
							}
					"""
			)
		);

		Directory.CreateDirectory(Path.GetDirectoryName(assetsPath)!);
		File.WriteAllText(
			assetsPath,
			$$"""
			{
				"version": 3,
				"libraries": {
			{{libraries}}
				}
			}
			"""
		);
	}

	/// <summary>
	/// Writes a <c>skills-lock.json</c> in the schema the skills CLI actually emits: a version, then
	/// one flat entry per installed skill carrying its source, the ref it was pinned to (absent when
	/// the install was not pinned), the source type and the content hash.
	/// </summary>
	public static void WriteLockFile(string lockFilePath, params (string Name, string Source, string? Ref)[] entries)
	{
		var skills = string.Join(
			",\n",
			entries.Select(entry =>
			{
				var properties = new List<string> { $"\"source\": \"{entry.Source}\"" };
				if (entry.Ref is not null)
					properties.Add($"\"ref\": \"{entry.Ref}\"");

				properties.Add("\"sourceType\": \"github\"");
				properties.Add($"\"skillPath\": \".agents/skills/{entry.Name}/SKILL.md\"");
				properties.Add(
					"\"computedHash\": \"7d83b50f4bb17da819387f9c155978d863eaa457c11821c29931b164ef0d1024\""
				);

				return $"    \"{entry.Name}\": {{ {string.Join(", ", properties)} }}";
			})
		);

		Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
		File.WriteAllText(lockFilePath, $"{{\n  \"version\": 1,\n  \"skills\": {{\n{skills}\n  }}\n}}\n");
	}
}

#pragma warning restore CA1308
