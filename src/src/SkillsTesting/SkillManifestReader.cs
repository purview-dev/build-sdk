namespace Purview.Skills.Testing;

/// <summary>Reads a <c>SKILL.md</c> into a <see cref="SkillManifest"/>.</summary>
/// <remarks>
/// <para>
/// The frontmatter parser covers the subset of YAML the Agent Skills specification defines - scalar
/// fields, block scalars, and a one-level <c>metadata</c> mapping - rather than taking a YAML
/// dependency. That is deliberate: this assembly is referenced by a consuming repository's test
/// project, and a transitive YAML parser is a dependency conflict waiting to happen for a file
/// format whose whole surface is six fields.
/// </para>
/// <para>
/// Anything the parser cannot make sense of becomes a
/// <see cref="SkillManifest.StructuralErrors">structural error</see> rather than being quietly
/// dropped, because a silently-ignored field reads as a passing skill.
/// </para>
/// </remarks>
public static class SkillManifestReader
{
	/// <summary>Field names the specification defines at the top level of the frontmatter.</summary>
	public static IReadOnlyList<string> KnownFields { get; } =
	["name", "description", "license", "compatibility", "metadata", "allowed-tools"];

	/// <summary>Reads the skill in a folder.</summary>
	/// <param name="skillDirectory">The folder, whose name the skill's name must match.</param>
	/// <returns>The manifest, including one describing a missing or malformed file.</returns>
	public static SkillManifest Read(string skillDirectory)
	{
		ArgumentException.ThrowIfNullOrEmpty(skillDirectory);

		var folderName = Path.GetFileName(
			skillDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
		);
		var skillFilePath = Path.Combine(skillDirectory, "SKILL.md");

		if (!File.Exists(skillFilePath))
		{
			return new SkillManifest
			{
				FolderName = folderName,
				DirectoryPath = skillDirectory,
				SkillFilePath = skillFilePath,
				Exists = false,
				StructuralErrors = ["no SKILL.md. A skill folder without one is invisible to the skills CLI."],
			};
		}

		return Parse(folderName, skillDirectory, skillFilePath, File.ReadAllText(skillFilePath));
	}

	/// <summary>Parses the text of a <c>SKILL.md</c>.</summary>
	/// <param name="folderName">The owning folder's name.</param>
	/// <param name="skillDirectory">The owning folder's path.</param>
	/// <param name="skillFilePath">The file's path.</param>
	/// <param name="text">The file's content.</param>
	/// <returns>The manifest.</returns>
	public static SkillManifest Parse(string folderName, string skillDirectory, string skillFilePath, string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

		SkillManifest manifest = new()
		{
			FolderName = folderName,
			DirectoryPath = skillDirectory,
			SkillFilePath = skillFilePath,
			Exists = true,
			LineCount = lines.Length,
		};

		if (lines.Length == 0 || lines[0].TrimEnd() != "---")
		{
			return manifest with
			{
				StructuralErrors = ["SKILL.md does not open with YAML frontmatter ('---' on the first line)."],
				Body = text,
			};
		}

		var closing = Array.FindIndex(lines, 1, line => line.TrimEnd() == "---");
		if (closing < 0)
		{
			return manifest with
			{
				StructuralErrors = ["SKILL.md frontmatter is not terminated by a closing '---'."],
				Body = string.Empty,
			};
		}

		List<string> errors = [];
		List<string> unknown = [];
		Dictionary<string, string> fields = new(StringComparer.Ordinal);
		Dictionary<string, string> metadata = new(StringComparer.Ordinal);

		for (var index = 1; index < closing; index++)
		{
			var line = lines[index];
			if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
				continue;

			// An indented line belongs to the key above it, and that key consumed it already.
			if (char.IsWhiteSpace(line[0]))
			{
				errors.Add($"frontmatter line {index + 1} is indented but does not belong to a key: '{line.Trim()}'.");
				continue;
			}

			var separator = line.IndexOf(':', StringComparison.Ordinal);
			if (separator <= 0)
			{
				errors.Add($"frontmatter line {index + 1} is not a 'key: value' pair: '{line.Trim()}'.");
				continue;
			}

			var key = line[..separator].Trim();
			var value = line[(separator + 1)..].Trim();

			if (fields.ContainsKey(key) || (key == "metadata" && metadata.Count > 0))
			{
				errors.Add($"frontmatter declares '{key}' more than once.");
				continue;
			}

			if (!KnownFields.Contains(key, StringComparer.Ordinal))
				unknown.Add(key);

			if (key == "metadata")
			{
				index = ReadMapping(lines, index, closing, metadata, errors);
				fields[key] = string.Empty;
				continue;
			}

			if (IsBlockScalarHeader(value))
			{
				index = ReadBlockScalar(lines, index, closing, value, out var folded);
				fields[key] = folded;
				continue;
			}

			fields[key] = Unquote(value);
		}

		return manifest with
		{
			Name = fields.GetValueOrDefault("name"),
			Description = fields.GetValueOrDefault("description"),
			License = fields.GetValueOrDefault("license"),
			Compatibility = fields.GetValueOrDefault("compatibility"),
			AllowedTools = fields.GetValueOrDefault("allowed-tools"),
			Metadata = metadata,
			UnknownFields = unknown,
			StructuralErrors = errors,
			Body = string.Join('\n', lines[(closing + 1)..]),
		};
	}

	/// <summary>Reports whether a value introduces a YAML block scalar.</summary>
	/// <param name="value">The inline value.</param>
	/// <returns><see langword="true"/> for the folded and literal block indicators.</returns>
	static bool IsBlockScalarHeader(string value) => value is ">" or ">-" or ">+" or "|" or "|-" or "|+";

	/// <summary>Consumes the indented lines of a block scalar.</summary>
	/// <param name="lines">Every line in the file.</param>
	/// <param name="headerIndex">The index of the line carrying the indicator.</param>
	/// <param name="closing">The index of the closing frontmatter delimiter.</param>
	/// <param name="indicator">The block indicator.</param>
	/// <param name="value">The assembled value.</param>
	/// <returns>The index of the last line consumed.</returns>
	static int ReadBlockScalar(string[] lines, int headerIndex, int closing, string indicator, out string value)
	{
		List<string> content = [];
		var index = headerIndex;

		while (index + 1 < closing && (lines[index + 1].Trim().Length == 0 || char.IsWhiteSpace(lines[index + 1][0])))
		{
			index++;
			content.Add(lines[index].Trim());
		}

		// Folded ('>') joins with spaces, literal ('|') keeps the line breaks. Either way the result
		// is only ever measured and tested for emptiness, so trailing-newline chomping is irrelevant.
		value = indicator.StartsWith('>')
			? string.Join(' ', content.Where(entry => entry.Length > 0))
			: string.Join('\n', content).Trim();

		return index;
	}

	/// <summary>Consumes the indented <c>key: value</c> lines of a mapping.</summary>
	/// <param name="lines">Every line in the file.</param>
	/// <param name="headerIndex">The index of the mapping's key line.</param>
	/// <param name="closing">The index of the closing frontmatter delimiter.</param>
	/// <param name="mapping">The accumulating mapping.</param>
	/// <param name="errors">The accumulating structural errors.</param>
	/// <returns>The index of the last line consumed.</returns>
	static int ReadMapping(
		string[] lines,
		int headerIndex,
		int closing,
		Dictionary<string, string> mapping,
		List<string> errors
	)
	{
		var index = headerIndex;

		while (index + 1 < closing && (lines[index + 1].Trim().Length == 0 || char.IsWhiteSpace(lines[index + 1][0])))
		{
			index++;

			var entry = lines[index].Trim();
			if (entry.Length == 0)
				continue;

			// The spec defines metadata as a map from string keys to string values, so a nested
			// sequence or mapping is out of contract and is reported rather than flattened.
			if (entry.StartsWith('-'))
			{
				errors.Add(
					$"'metadata' holds a sequence at line {index + 1}; the specification defines it as a map of string to string."
				);
				continue;
			}

			var separator = entry.IndexOf(':', StringComparison.Ordinal);
			if (separator <= 0)
			{
				errors.Add($"'metadata' entry at line {index + 1} is not a 'key: value' pair: '{entry}'.");
				continue;
			}

			var key = entry[..separator].Trim();
			var value = Unquote(entry[(separator + 1)..].Trim());

			if (value.Length == 0)
			{
				errors.Add($"'metadata.{key}' has no value; the specification defines metadata values as strings.");
				continue;
			}

			mapping[key] = value;
		}

		return index;
	}

	/// <summary>Strips one layer of matching quotes from a scalar.</summary>
	/// <param name="value">The raw value.</param>
	/// <returns>The unquoted value.</returns>
	static string Unquote(string value) =>
		value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
			? value[1..^1]
			: value;
}
