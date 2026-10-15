using System.Text.Json;
using System.Text.Json.Serialization;

namespace Purview.Architecture.Testing;

/// <summary>
/// The dependency and naming policy, read from <c>.config/architecture-policy.json</c>.
/// </summary>
/// <remarks>
/// The policy file is the single definition of each rule. The tests read it, and
/// <c>PolicyDocumentationTests</c> asserts the wiki agrees with it, so a rule cannot be true in prose
/// and false in code. See <c>docs/wiki/Project-Responsibilities.md</c>.
/// </remarks>
public sealed class ArchitecturePolicy
{
	static readonly JsonSerializerOptions SerializerOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	[JsonPropertyName("version")]
	public int Version { get; init; }

	/// <summary>
	/// Repo-relative folders holding the projects. An empty array scans the repository root.
	/// </summary>
	/// <remarks>
	/// Defaults to <c>src</c>, which is the Purview layout. A repository with a different shape must
	/// set this, or discovery finds nothing - which the GraphDiscovery rule reports rather than
	/// letting every other rule pass vacuously.
	/// </remarks>
	[JsonPropertyName("sourceRoots")]
	public IReadOnlyList<string> SourceRoots { get; init; } = ["src"];

	[JsonPropertyName("responsibilities")]
	public IReadOnlyList<Responsibility> Responsibilities { get; init; } = [];

	[JsonPropertyName("discouragedProjectNames")]
	public DiscouragedNames DiscouragedProjectNames { get; init; } = new();

	[JsonPropertyName("rules")]
	public PolicyRules Rules { get; init; } = new();

	[JsonPropertyName("waivers")]
	public IReadOnlyList<PolicyWaiver> Waivers { get; init; } = [];

	/// <summary>Finds the documented waiver for a project, if one exists.</summary>
	/// <param name="projectName">The project name.</param>
	/// <returns>The waiver, or <see langword="null"/>.</returns>
	public PolicyWaiver? WaiverFor(string projectName) =>
		Waivers.FirstOrDefault(waiver =>
			string.Equals(waiver.Project, projectName, StringComparison.OrdinalIgnoreCase)
		);

	/// <summary>The policy file's location relative to a repository root.</summary>
	public static string RelativePath { get; } = Path.Combine(".config", "architecture-policy.json");

	/// <summary>Reads the policy from a repository's configuration folder.</summary>
	/// <param name="repositoryRoot">The repository root, or <see langword="null"/> to probe upward for one.</param>
	/// <returns>The policy.</returns>
	/// <exception cref="FileNotFoundException">No policy file exists at the expected location.</exception>
	public static ArchitecturePolicy Load(string? repositoryRoot = null)
	{
		var path = Path.Combine(repositoryRoot ?? RepositoryRoot(), RelativePath);
		if (!File.Exists(path))
		{
			throw new FileNotFoundException(
				$"The architecture policy was not found at '{path}'. Add one, pointing its $schema at "
					+ "https://raw.githubusercontent.com/purview-dev/build-sdk/main/architecture-policy.schema.json",
				path
			);
		}

		return JsonSerializer.Deserialize<ArchitecturePolicy>(File.ReadAllText(path), SerializerOptions)
			?? throw new InvalidOperationException($"The architecture policy at '{path}' deserialised to null.");
	}

	/// <summary>Finds the repository root by walking up to the folder holding the policy.</summary>
	/// <returns>The repository root path.</returns>
	/// <remarks>
	/// The test binary runs from <c>bin/</c>, and the depth of that path differs between a local build
	/// and CI, so the root is found by probing for a marker rather than by counting parents.
	/// </remarks>
	public static string RepositoryRoot()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, RelativePath)))
				return directory.FullName;

			// Stop at the repository boundary. Walking past it would happily find a policy belonging
			// to a parent folder - a sibling checkout, or a stray file in the directory everything is
			// cloned into - and silently validate this repository against someone else's rules.
			if (
				Directory.Exists(Path.Combine(directory.FullName, ".git"))
				|| File.Exists(Path.Combine(directory.FullName, ".git"))
			)
			{
				throw new InvalidOperationException(
					$"No architecture policy was found in the repository at '{directory.FullName}'. "
						+ $"Add '{RelativePath}', or override RepositoryRoot on the test class. "
						+ "The search deliberately stops at the repository boundary rather than continuing into parent folders."
				);
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException(
			$"Could not locate the repository root above '{AppContext.BaseDirectory}'. Expected a '.config/architecture-policy.json'."
		);
	}

	/// <summary>Finds the responsibility a project name maps to, honouring aliases.</summary>
	/// <param name="projectName">The project name, which may be implementation-qualified.</param>
	/// <returns>The responsibility, or <see langword="null"/> when the name is not recognised.</returns>
	/// <remarks>
	/// A project name is <c>&lt;Responsibility&gt;[.&lt;Implementation&gt;]</c>, so only the first
	/// segment is classified: <c>Persistence.SqlServer</c> and <c>Persistence</c> are both Persistence.
	/// Qualification is a naming choice, never a different set of rules.
	/// </remarks>
	public Responsibility? Classify(string projectName)
	{
		ArgumentNullException.ThrowIfNull(projectName);

		// A documented exception maps an established project onto the responsibility it actually has,
		// so the dependency rules still apply in full - only the name is excused.
		var waiver = WaiverFor(projectName);
		if (waiver is not null && !string.IsNullOrEmpty(waiver.MapsTo))
		{
			return Responsibilities.FirstOrDefault(responsibility =>
				string.Equals(responsibility.Name, waiver.MapsTo, StringComparison.OrdinalIgnoreCase)
			);
		}

		var segment = projectName.Split('.')[0];

		return Responsibilities.FirstOrDefault(responsibility =>
			string.Equals(responsibility.Name, segment, StringComparison.OrdinalIgnoreCase)
			|| responsibility.Aliases.Any(alias => string.Equals(alias, segment, StringComparison.OrdinalIgnoreCase))
		);
	}
}

/// <summary>A single architectural responsibility and the dependencies it permits.</summary>
public sealed class Responsibility
{
	[JsonPropertyName("name")]
	public string Name { get; init; } = string.Empty;

	[JsonPropertyName("aliases")]
	public IReadOnlyList<string> Aliases { get; init; } = [];

	[JsonPropertyName("category")]
	public string Category { get; init; } = string.Empty;

	[JsonPropertyName("purpose")]
	public string Purpose { get; init; } = string.Empty;

	[JsonPropertyName("mayDependOn")]
	public IReadOnlyList<string> MayDependOn { get; init; } = [];

	[JsonPropertyName("mustNotDependOn")]
	public IReadOnlyList<string> MustNotDependOn { get; init; } = [];

	[JsonPropertyName("forbiddenPackagePrefixes")]
	public IReadOnlyList<string> ForbiddenPackagePrefixes { get; init; } = [];

	[JsonPropertyName("siblingIndependence")]
	public bool SiblingIndependence { get; init; }

	[JsonPropertyName("separateProjectWhen")]
	public string SeparateProjectWhen { get; init; } = string.Empty;

	[JsonPropertyName("mayRemainCombinedWhen")]
	public string MayRemainCombinedWhen { get; init; } = string.Empty;
}

/// <summary>Project names that describe a category rather than a responsibility.</summary>
public sealed class DiscouragedNames
{
	[JsonPropertyName("names")]
	public IReadOnlyList<string> Names { get; init; } = [];

	[JsonPropertyName("reason")]
	public string Reason { get; init; } = string.Empty;
}

/// <summary>A deliberate, reviewed departure from the policy for one project.</summary>
/// <remarks>
/// An exception is how a rule stays enforceable in a repository that predates it. Suppressing a rule
/// silently would make the policy a lie; recording it here keeps the departure reviewable and keeps
/// every other rule in force for that project.
/// </remarks>
public sealed class PolicyWaiver
{
	[JsonPropertyName("project")]
	public string Project { get; init; } = string.Empty;

	[JsonPropertyName("mapsTo")]
	public string MapsTo { get; init; } = string.Empty;

	[JsonPropertyName("rules")]
	public IReadOnlyList<string> Rules { get; init; } = [];

	[JsonPropertyName("reason")]
	public string Reason { get; init; } = string.Empty;

	[JsonPropertyName("reviewedOn")]
	public string ReviewedOn { get; init; } = string.Empty;

	/// <summary>Reports whether this exception excuses a named rule.</summary>
	/// <param name="rule">The rule identifier.</param>
	/// <returns><see langword="true"/> when the rule is excused.</returns>
	public bool Excuses(string rule) => Rules.Contains(rule, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Repository-wide rules that are not expressed per responsibility.</summary>
public sealed class PolicyRules
{
	[JsonPropertyName("noCycles")]
	public bool NoCycles { get; init; }

	[JsonPropertyName("noProductionToTestReferences")]
	public bool NoProductionToTestReferences { get; init; }

	[JsonPropertyName("implementationsMustNotReferenceComposition")]
	public bool ImplementationsMustNotReferenceComposition { get; init; }

	[JsonPropertyName("siblingImplementationsIndependent")]
	public bool SiblingImplementationsIndependent { get; init; }
}
