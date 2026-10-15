namespace Purview.Architecture.Testing;

/// <summary>
/// Evaluates a repository's project graph against an architecture policy.
/// </summary>
/// <remarks>
/// Deliberately free of MSBuild and test-framework types: the rules are the part worth testing
/// directly, and coupling them to a test base class would make them reachable only through one.
/// <see cref="PurviewArchitectureTests"/> is a thin wrapper over this.
/// </remarks>
public sealed class ArchitecturePolicyEvaluator
{
	readonly ArchitecturePolicy _policy;
	readonly IReadOnlyDictionary<string, ProjectNode> _projects;

	/// <summary>Creates an evaluator over an explicit policy and graph.</summary>
	/// <param name="policy">The policy to apply.</param>
	/// <param name="projects">The projects to evaluate, keyed by name.</param>
	public ArchitecturePolicyEvaluator(ArchitecturePolicy policy, IReadOnlyDictionary<string, ProjectNode> projects)
	{
		ArgumentNullException.ThrowIfNull(policy);
		ArgumentNullException.ThrowIfNull(projects);

		_policy = policy;
		_projects = projects;
	}

	/// <summary>Creates an evaluator for a repository on disk.</summary>
	/// <param name="repositoryRoot">The repository root, or <see langword="null"/> to probe upward for one.</param>
	/// <returns>The evaluator.</returns>
	public static ArchitecturePolicyEvaluator ForRepository(string? repositoryRoot = null)
	{
		var root = repositoryRoot ?? ArchitecturePolicy.RepositoryRoot();

		var policy = ArchitecturePolicy.Load(root);

		return new ArchitecturePolicyEvaluator(policy, ProjectGraph.Load(root, policy.SourceRoots));
	}

	/// <summary>The policy being applied.</summary>
	public ArchitecturePolicy Policy => _policy;

	/// <summary>The production projects, excluding test projects.</summary>
	public IEnumerable<ProjectNode> ProductionProjects => _projects.Values.Where(project => !project.IsTestProject);

	/// <summary>Runs every rule.</summary>
	/// <returns>Every violation found, in rule order.</returns>
	public IReadOnlyList<ArchitectureViolation> EvaluateAll() =>
		[
			.. GraphDiscovery(),
			.. Naming(),
			.. DiscouragedNames(),
			.. DependencyMatrix(),
			.. SiblingIndependence(),
			.. CompositionBoundary(),
			.. ForbiddenPackages(),
			.. Cycles(),
			.. TestReferences(),
			.. WaiverIntegrity(),
		];

	/// <summary>Reports when no projects were discovered at all.</summary>
	/// <returns>The violations.</returns>
	/// <remarks>
	/// Every other rule is satisfied by an empty graph, so without this a repository whose layout the
	/// scan does not match gets a completely green architecture suite that checked nothing. Emptiness
	/// has to be the loud failure, not the quiet success.
	/// </remarks>
	public IReadOnlyList<ArchitectureViolation> GraphDiscovery() =>
		ProductionProjects.Any()
			? []
			:
			[
				new ArchitectureViolation(
					ArchitectureRules.GraphDiscovery,
					"(repository)",
					"No production projects were discovered, so every other rule would pass without checking anything. "
						+ $"Looked under: {(_policy.SourceRoots.Count == 0 ? "(repository root)" : string.Join(", ", _policy.SourceRoots))}. "
						+ "Set 'sourceRoots' in the policy to the folders holding the projects; an empty array scans the repository root."
				),
			];

	/// <summary>Projects whose names map to no responsibility.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<ArchitectureViolation> Naming() =>
		[
			.. ProductionProjects
				.Where(project =>
					_policy.Classify(project.Name) is null
					&& _policy.WaiverFor(project.Name)?.Excuses(ArchitectureRules.Naming) != true
				)
				.Select(project => new ArchitectureViolation(
					ArchitectureRules.Naming,
					project.Name,
					$"'{project.Name}' does not map to a known responsibility or alias. "
						+ "Rename it to '<Responsibility>[.<Implementation>]', or record a waiver in the policy. "
						+ $"Known responsibilities: {string.Join(", ", _policy.Responsibilities.Select(responsibility => responsibility.Name))}."
				)),
		];

	/// <summary>Projects named after a category rather than a responsibility.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<ArchitectureViolation> DiscouragedNames() =>
		[
			.. ProductionProjects
				.Where(project =>
					_policy.DiscouragedProjectNames.Names.Contains(
						project.Name.Split('.')[0],
						StringComparer.OrdinalIgnoreCase
					)
					&& _policy.WaiverFor(project.Name)?.Excuses(ArchitectureRules.DiscouragedName) != true
				)
				.Select(project => new ArchitectureViolation(
					ArchitectureRules.DiscouragedName,
					project.Name,
					$"'{project.Name}': {_policy.DiscouragedProjectNames.Reason}"
				)),
		];

	/// <summary>References the dependency matrix forbids or does not permit.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<ArchitectureViolation> DependencyMatrix()
	{
		List<ArchitectureViolation> violations = [];

		foreach (var project in ProductionProjects)
		{
			var responsibility = _policy.Classify(project.Name);
			if (responsibility is null)
				continue;

			foreach (var reference in project.ProjectReferences)
			{
				var referenced = _policy.Classify(reference);
				if (referenced is null)
					continue;

				// A qualified sibling is the same box on the diagram, so sibling independence governs
				// it rather than the matrix.
				if (string.Equals(referenced.Name, responsibility.Name, StringComparison.OrdinalIgnoreCase))
					continue;

				if (responsibility.MustNotDependOn.Contains(referenced.Name, StringComparer.OrdinalIgnoreCase))
				{
					violations.Add(
						new ArchitectureViolation(
							ArchitectureRules.DependencyMatrix,
							project.Name,
							$"'{project.Name}' ({responsibility.Name}) references '{reference}' ({referenced.Name}), "
								+ $"but {responsibility.Name} must not depend on {referenced.Name}. "
								+ $"Invert it: {referenced.Name} may depend on {responsibility.Name}, or introduce an abstraction "
								+ $"that {responsibility.Name} owns and {referenced.Name} implements."
						)
					);
				}
				else if (!responsibility.MayDependOn.Contains(referenced.Name, StringComparer.OrdinalIgnoreCase))
				{
					violations.Add(
						new ArchitectureViolation(
							ArchitectureRules.DependencyMatrix,
							project.Name,
							$"'{project.Name}' ({responsibility.Name}) references '{reference}' ({referenced.Name}), "
								+ "which the policy neither permits nor forbids. "
								+ $"{responsibility.Name} may depend on: {string.Join(", ", responsibility.MayDependOn)}. "
								+ $"Add {referenced.Name} to mayDependOn in the policy if this is intended."
						)
					);
				}
			}
		}

		return violations;
	}

	/// <summary>Qualified implementations referencing a qualified sibling.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<ArchitectureViolation> SiblingIndependence()
	{
		List<ArchitectureViolation> violations = [];

		foreach (var project in ProductionProjects)
		{
			var responsibility = _policy.Classify(project.Name);
			if (responsibility?.SiblingIndependence != true || !project.Name.Contains('.', StringComparison.Ordinal))
				continue;

			foreach (var reference in project.ProjectReferences)
			{
				// Only a qualified sibling is a peer; the unqualified project is shared ground.
				var isQualifiedSibling =
					reference.Contains('.', StringComparison.Ordinal)
					&& !string.Equals(reference, project.Name, StringComparison.OrdinalIgnoreCase)
					&& string.Equals(
						_policy.Classify(reference)?.Name,
						responsibility.Name,
						StringComparison.OrdinalIgnoreCase
					);

				if (isQualifiedSibling)
				{
					violations.Add(
						new ArchitectureViolation(
							ArchitectureRules.SiblingIndependence,
							project.Name,
							$"'{project.Name}' references its sibling '{reference}'. Sibling {responsibility.Name} implementations "
								+ $"must stay independent; move the shared part into the unqualified '{responsibility.Name}' project "
								+ "or into an abstraction both can depend on."
						)
					);
				}
			}
		}

		return violations;
	}

	/// <summary>Non-host projects referencing Composition.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<ArchitectureViolation> CompositionBoundary() =>
		[
			.. ProductionProjects
				.Where(project =>
					!string.Equals(
						_policy.Classify(project.Name)?.Name,
						"Composition",
						StringComparison.OrdinalIgnoreCase
					)
					&& !string.Equals(
						_policy.Classify(project.Name)?.Category,
						"host",
						StringComparison.OrdinalIgnoreCase
					)
					&& project.ProjectReferences.Any(reference =>
						string.Equals(
							_policy.Classify(reference)?.Name,
							"Composition",
							StringComparison.OrdinalIgnoreCase
						)
					)
				)
				.Select(project => new ArchitectureViolation(
					ArchitectureRules.CompositionBoundary,
					project.Name,
					$"'{project.Name}' references Composition. Composition selects implementations, so it depends on them - "
						+ "never the other way round, which would make the choice circular."
				)),
		];

	/// <summary>Packages matching a forbidden prefix for their responsibility.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<ArchitectureViolation> ForbiddenPackages()
	{
		List<ArchitectureViolation> violations = [];

		foreach (var project in ProductionProjects)
		{
			var responsibility = _policy.Classify(project.Name);
			if (responsibility is null)
				continue;

			foreach (var package in project.PackageReferences)
			{
				var forbidden = responsibility.ForbiddenPackagePrefixes.FirstOrDefault(prefix =>
					package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
				);

				if (forbidden is not null)
				{
					violations.Add(
						new ArchitectureViolation(
							ArchitectureRules.ForbiddenPackage,
							project.Name,
							$"'{project.Name}' ({responsibility.Name}) references package '{package}', which matches the forbidden "
								+ $"prefix '{forbidden}' for {responsibility.Name}. {responsibility.Purpose} "
								+ "Move the dependency to the responsibility that owns that technology."
						)
					);
				}
			}
		}

		return violations;
	}

	/// <summary>Cycles in the project dependency graph.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<ArchitectureViolation> Cycles() =>
		[
			.. ProjectGraph
				.FindCycles(_projects)
				.Select(cycle => new ArchitectureViolation(
					ArchitectureRules.Cycle,
					cycle,
					$"Dependency cycle: {cycle}. Break it by moving the shared part into a project both can depend on."
				)),
		];

	/// <summary>Production projects referencing a test project.</summary>
	/// <returns>The violations.</returns>
	public IReadOnlyList<ArchitectureViolation> TestReferences() =>
		[
			.. ProductionProjects.SelectMany(project =>
				project
					.ProjectReferences.Where(reference =>
						_projects.TryGetValue(reference, out var target) && target.IsTestProject
					)
					.Select(reference => new ArchitectureViolation(
						ArchitectureRules.TestReference,
						project.Name,
						$"'{project.Name}' references the test project '{reference}'. Test helpers must not ship."
					))
			),
		];

	/// <summary>Waivers that are stale, unjustified or malformed.</summary>
	/// <returns>The violations.</returns>
	/// <remarks>
	/// A waiver nobody revisits is how a policy quietly stops being true, so the waivers are
	/// themselves subject to rules.
	/// </remarks>
	public IReadOnlyList<ArchitectureViolation> WaiverIntegrity()
	{
		List<ArchitectureViolation> violations = [];

		foreach (var waiver in _policy.Waivers)
		{
			if (!_projects.ContainsKey(waiver.Project))
			{
				violations.Add(
					new ArchitectureViolation(
						ArchitectureRules.WaiverIntegrity,
						waiver.Project,
						$"The waiver for '{waiver.Project}' names a project that does not exist. Remove it - a stale waiver hides the next one."
					)
				);
			}

			if (waiver.Rules.Count == 0)
			{
				violations.Add(
					new ArchitectureViolation(
						ArchitectureRules.WaiverIntegrity,
						waiver.Project,
						$"The waiver for '{waiver.Project}' excuses no named rule, so it does nothing."
					)
				);
			}

			if (waiver.MapsTo.Length > 0 && _policy.Classify(waiver.Project) is null)
			{
				violations.Add(
					new ArchitectureViolation(
						ArchitectureRules.WaiverIntegrity,
						waiver.Project,
						$"The waiver for '{waiver.Project}' maps to unknown responsibility '{waiver.MapsTo}', so no dependency rule applies to it."
					)
				);
			}
		}

		return violations;
	}

	/// <summary>Renders violations as a numbered list for a failure message.</summary>
	/// <param name="violations">The violations to render.</param>
	/// <returns>The rendered report.</returns>
	public static string Report(IReadOnlyList<ArchitectureViolation> violations)
	{
		ArgumentNullException.ThrowIfNull(violations);

		return violations.Count == 0
			? "no violations"
			: Environment.NewLine
				+ string.Join(
					Environment.NewLine,
					violations.Select((violation, index) => $"  {index + 1}. {violation.Message}")
				);
	}
}
