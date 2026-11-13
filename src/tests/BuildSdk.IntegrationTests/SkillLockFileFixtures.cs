namespace Purview.BuildSdk;

/// <summary>
/// Writes the skills CLI's own <c>skills-lock.json</c> directly, so the skill target tests stay offline
/// and need no Node tooling.
/// </summary>
static class SkillLockFileFixtures
{
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
