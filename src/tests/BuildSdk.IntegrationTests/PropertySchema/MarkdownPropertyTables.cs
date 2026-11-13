using System.Text.RegularExpressions;

namespace Purview.BuildSdk.PropertySchema;

/// <summary>A property row in a markdown table: <c>| `Name` | ... |</c>.</summary>
sealed record MarkdownPropertyRow(string File, int Line, string Section, string Name, string? DefaultCell);

/// <summary>
/// Reads the property tables in a markdown document. A table counts when its first column is a
/// backticked property name. Its default is read from a column headed <c>Default</c>, when it has one.
/// </summary>
static partial class MarkdownPropertyTables
{
	/// <summary>The heading of the table that lists every compiler-visible property.</summary>
	public const string CompilerVisibleSection = "Compiler-visible SDK properties";

	[GeneratedRegex(@"^#{1,6}\s+(?<title>.+?)\s*$")]
	private static partial Regex HeadingPattern();

	[GeneratedRegex(@"^\|\s*`(?<name>[A-Za-z_][A-Za-z0-9_]*)`\s*\|")]
	private static partial Regex PropertyRowPattern();

	[GeneratedRegex(@"^`(?<literal>[^`]*)`")]
	private static partial Regex LeadingLiteralPattern();

	public static IReadOnlyList<MarkdownPropertyRow> Read(string path, string displayPath)
	{
		List<MarkdownPropertyRow> rows = [];
		var section = "";
		var defaultColumn = -1;
		var inPropertyTable = false;
		var inFence = false;
		var lines = File.ReadAllLines(path);

		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
			{
				inFence = !inFence;
				continue;
			}

			if (inFence)
				continue;

			var heading = HeadingPattern().Match(line);
			if (heading.Success)
			{
				section = heading.Groups["title"].Value;
				defaultColumn = -1;
				inPropertyTable = false;
				continue;
			}

			if (line.Length == 0 || line[0] != '|')
			{
				defaultColumn = -1;
				inPropertyTable = false;
				continue;
			}

			var cells = SplitCells(line);
			if (i + 1 < lines.Length && lines[i + 1].StartsWith("| --", StringComparison.Ordinal))
			{
				// A header row. Only a table headed "Property" lists properties; diagnostic, target and
				// mapping tables also open their rows with a backticked name.
				inPropertyTable = cells[0] == "Property";
				defaultColumn = inPropertyTable ? Array.FindIndex(cells, c => c == "Default") : -1;
				continue;
			}

			var row = PropertyRowPattern().Match(line);
			if (!inPropertyTable || !row.Success)
				continue;

			var defaultCell = defaultColumn > 0 && defaultColumn < cells.Length ? cells[defaultColumn] : null;
			rows.Add(new(displayPath, i + 1, section, row.Groups["name"].Value, defaultCell));
		}

		return rows;
	}

	/// <summary>
	/// The literal a default cell states: <c>`true`</c> and <c>`false` (`true` for X)</c> both state
	/// <c>true</c>/<c>false</c>. A cell that opens with prose, such as <c>*(detected)*</c>, states none.
	/// </summary>
	public static string? StatedDefault(string? cell)
	{
		if (cell is null)
			return null;

		var match = LeadingLiteralPattern().Match(cell);
		return match.Success ? match.Groups["literal"].Value : null;
	}

	/// <summary>
	/// A list default is written without the SDK's internal <c>;</c> fences, so a document stating
	/// <c>CA1002;CA1012</c> agrees with a value of <c>;CA1002;CA1012;</c>.
	/// </summary>
	public static string NormaliseList(string value) => value.Trim().Trim(';');

	static string[] SplitCells(string line)
	{
		// Escaped pipes (\|) never appear in these tables; a plain split is enough.
		var trimmed = line.Trim().Trim('|');
		return [.. trimmed.Split('|').Select(c => c.Trim())];
	}
}
