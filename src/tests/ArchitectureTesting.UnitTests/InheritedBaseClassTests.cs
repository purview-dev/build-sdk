namespace Purview.Architecture.Testing.UnitTests;

/// <summary>
/// Proves TUnit discovers the tests a consuming repository inherits from
/// <see cref="PurviewArchitectureTests"/>.
/// </summary>
/// <remarks>
/// The whole adoption story is "add a policy file and one derived class", so test discovery through
/// inheritance is load-bearing. If TUnit did not walk base types, a consumer would add the class,
/// see a green run, and be enforcing nothing at all - the worst possible failure for this package.
///
/// It points at a fixture repository rather than this one: build-sdk's own projects (BuildSdk,
/// Analyzers, CodeFixers) are an MSBuild SDK and Roslyn components, which the responsibility
/// catalogue does not describe.
/// </remarks>
[InheritsTests]
public sealed class InheritedBaseClassTests : PurviewArchitectureTests
{
	static readonly Lazy<string> Fixture = new(CreateFixtureRepository);

	/// <inheritdoc />
	protected override string? RepositoryRoot => Fixture.Value;

	/// <summary>
	/// Writes a minimal, policy-conformant repository to a temporary folder.
	/// </summary>
	/// <returns>The fixture repository root.</returns>
	static string CreateFixtureRepository()
	{
		var root = Path.Combine(Path.GetTempPath(), "PurviewArchitectureFixture", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(root, ".config"));

		File.WriteAllText(
			Path.Combine(root, ".config", "architecture-policy.json"),
			$$"""
			{
				"$schema": "{{PolicySchemaValidator.SchemaId}}",
				"version": 1,
				"responsibilities": [
					{
						"name": "Core",
						"aliases": ["Domain"],
						"category": "domain",
						"purpose": "The fundamental domain model, its invariants and its rules.",
						"mayDependOn": [],
						"mustNotDependOn": ["Application", "Persistence"]
					},
					{
						"name": "Application",
						"category": "application",
						"purpose": "Coordinates the application's use cases and workflows.",
						"mayDependOn": ["Core"],
						"mustNotDependOn": ["Persistence"]
					},
					{
						"name": "Persistence",
						"category": "infrastructure",
						"purpose": "Durable data access and persistence-specific configuration.",
						"mayDependOn": ["Core", "Application"],
						"mustNotDependOn": []
					}
				]
			}
			"""
		);

		WriteProject(root, "Core");
		WriteProject(root, "Application", "Core");
		WriteProject(root, "Persistence", "Core");

		return root;
	}

	/// <summary>Writes a project file with the given project references.</summary>
	/// <param name="root">The fixture repository root.</param>
	/// <param name="name">The project name.</param>
	/// <param name="references">The projects it references.</param>
	static void WriteProject(string root, string name, params string[] references)
	{
		var directory = Path.Combine(root, "src", name);
		Directory.CreateDirectory(directory);

		var items = string.Concat(
			references.Select(reference => $"""<ProjectReference Include="../{reference}/{reference}.csproj" />""")
		);

		File.WriteAllText(
			Path.Combine(directory, $"{name}.csproj"),
			$"""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>{items}</ItemGroup></Project>"""
		);
	}
}
