using System.Xml.Linq;

namespace Purview.Architecture.Testing;

/// <summary>A project in the solution, with the references it declares.</summary>
/// <param name="Name">The project name, without extension.</param>
/// <param name="Path">The absolute path to the project file.</param>
/// <param name="ProjectReferences">The names of the projects it references directly.</param>
/// <param name="PackageReferences">The package identifiers it references directly.</param>
/// <param name="IsTestProject">Whether the project is a test project.</param>
public sealed record ProjectNode(
	string Name,
	string Path,
	IReadOnlyList<string> ProjectReferences,
	IReadOnlyList<string> PackageReferences,
	bool IsTestProject
);

/// <summary>
/// Reads the solution's projects and their declared references straight from the project files.
/// </summary>
/// <remarks>
/// Project files are read rather than compiled assemblies because a declared
/// <c>ProjectReference</c> is the thing a reviewer adds and the thing the policy governs. Compiled
/// assembly references are checked separately - they catch a dependency that arrived indirectly,
/// which the project file does not show.
/// </remarks>
public static class ProjectGraph
{
	/// <summary>Loads every project beneath the repository's source folder.</summary>
	/// <param name="repositoryRoot">The repository root.</param>
	/// <returns>The projects, keyed by name.</returns>
	public static IReadOnlyDictionary<string, ProjectNode> Load(string repositoryRoot)
	{
		var sourceRoot = Path.Combine(repositoryRoot, "src");
		Dictionary<string, ProjectNode> projects = new(StringComparer.OrdinalIgnoreCase);

		if (!Directory.Exists(sourceRoot))
			return projects;

		foreach (var projectPath in Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories))
		{
			// bin/ and obj/ can hold copies of project files during a build; they are not the source.
			if (
				projectPath.Contains(
					$"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
					StringComparison.Ordinal
				)
				|| projectPath.Contains(
					$"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
					StringComparison.Ordinal
				)
			)
			{
				continue;
			}

			var node = Read(projectPath);
			projects[node.Name] = node;
		}

		return projects;
	}

	/// <summary>Reads a single project file.</summary>
	/// <param name="projectPath">The project file path.</param>
	/// <returns>The project node.</returns>
	static ProjectNode Read(string projectPath)
	{
		var document = XDocument.Load(projectPath);
		var name = Path.GetFileNameWithoutExtension(projectPath);

		var projectReferences = document
			.Descendants("ProjectReference")
			.Select(element => (string?)element.Attribute("Include"))
			.Where(include => !string.IsNullOrWhiteSpace(include))
			.Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', Path.DirectorySeparatorChar)))
			.Where(reference => !string.IsNullOrWhiteSpace(reference))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var packageReferences = document
			.Descendants("PackageReference")
			.Select(element => (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update"))
			.Where(include => !string.IsNullOrWhiteSpace(include))
			.Select(include => include!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		// The SDK derives IsTestProject from the folder, so the path is the reliable signal here.
		var isTestProject =
			name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
			|| projectPath.Contains(
				$"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}",
				StringComparison.OrdinalIgnoreCase
			);

		return new ProjectNode(name, projectPath, projectReferences, packageReferences, isTestProject);
	}

	/// <summary>Finds every dependency cycle in the project graph.</summary>
	/// <param name="projects">The projects to inspect.</param>
	/// <returns>Each cycle, rendered as the path that closes it.</returns>
	/// <remarks>
	/// MSBuild itself rejects a cycle, so this is a guard that reports the cycle readably rather than
	/// as a build error naming two projects. It also covers a graph assembled in a test fixture.
	/// </remarks>
	public static IReadOnlyList<string> FindCycles(IReadOnlyDictionary<string, ProjectNode> projects)
	{
		ArgumentNullException.ThrowIfNull(projects);

		List<string> cycles = new();
		Dictionary<string, int> state = new();

		foreach (var project in projects.Keys)
			Visit(project, []);

		return cycles;

		void Visit(string current, List<string> path)
		{
			// 1 = on the current path, 2 = fully explored and clean.
			if (state.TryGetValue(current, out var visited))
			{
				if (visited == 1)
				{
					var start = path.FindIndex(entry =>
						string.Equals(entry, current, StringComparison.OrdinalIgnoreCase)
					);
					if (start >= 0)
						cycles.Add(string.Join(" -> ", path[start..].Append(current)));
				}

				return;
			}

			state[current] = 1;
			path.Add(current);

			if (projects.TryGetValue(current, out var node))
			{
				foreach (var reference in node.ProjectReferences)
					Visit(reference, path);
			}

			path.RemoveAt(path.Count - 1);
			state[current] = 2;
		}
	}
}
