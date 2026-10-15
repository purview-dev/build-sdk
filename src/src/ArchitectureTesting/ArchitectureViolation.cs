namespace Purview.Architecture.Testing;

/// <summary>A single breach of the architecture policy.</summary>
/// <param name="Rule">The rule identifier, so a caller can filter or waive by rule.</param>
/// <param name="Project">The offending project.</param>
/// <param name="Message">
/// What broke and what to do about it. A failure that only states the fact - "expected empty" -
/// costs the reader the whole investigation, so every message names the offending reference, the
/// direction the dependency should run, and the likely fix.
/// </param>
public sealed record ArchitectureViolation(string Rule, string Project, string Message)
{
	/// <inheritdoc />
	public override string ToString() => Message;
}

/// <summary>The rule identifiers the evaluator reports and a waiver may excuse.</summary>
public static class ArchitectureRules
{
	/// <summary>A project name that maps to no responsibility or alias.</summary>
	public const string Naming = "naming";

	/// <summary>A project named after a category rather than a responsibility.</summary>
	public const string DiscouragedName = "discouragedName";

	/// <summary>A reference the dependency matrix forbids or does not permit.</summary>
	public const string DependencyMatrix = "dependencyMatrix";

	/// <summary>A qualified implementation referencing a qualified sibling.</summary>
	public const string SiblingIndependence = "siblingIndependence";

	/// <summary>A non-host project referencing Composition.</summary>
	public const string CompositionBoundary = "compositionBoundary";

	/// <summary>A package whose identifier matches a forbidden prefix for the responsibility.</summary>
	public const string ForbiddenPackage = "forbiddenPackage";

	/// <summary>A cycle in the project dependency graph.</summary>
	public const string Cycle = "cycle";

	/// <summary>Production code referencing a test project.</summary>
	public const string TestReference = "testReference";

	/// <summary>A waiver that is stale, unjustified or malformed.</summary>
	public const string WaiverIntegrity = "waiverIntegrity";

	/// <summary>
	/// No projects were discovered, so every other rule would pass vacuously.
	/// </summary>
	/// <remarks>
	/// The most dangerous result this package can produce is a green run that checked nothing. An
	/// empty graph satisfies every rule by construction, so emptiness is itself a violation.
	/// </remarks>
	public const string GraphDiscovery = "graphDiscovery";
}
