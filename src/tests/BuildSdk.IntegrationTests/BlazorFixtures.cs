using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Project files and solution wiring shared by the Blazor WebAssembly, browser-safe and BFF tests.
/// </summary>
static class BlazorFixtures
{
	/// <summary>The automatic SDK packages, removed where a test is not about them.</summary>
	public const string RemoveAutomaticPackages = """
		<PackageReference Remove="Purview.Telemetry.SourceGenerator" />
		<PackageReference Remove="Microsoft.Extensions.Telemetry.Abstractions" />
		""";

	public static string Library(string properties = "", string items = "") =>
		$"""
			<Project Sdk="Microsoft.NET.Sdk">
				<PropertyGroup>
					<TargetFramework>net10.0</TargetFramework>
					<DisableSourceLink>true</DisableSourceLink>
					{properties}
				</PropertyGroup>
				<ItemGroup>
					{items}
				</ItemGroup>
			</Project>
			""";

	public static string Client(string properties = "", string items = "") =>
		$"""
			<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
				<PropertyGroup>
					<TargetFramework>net10.0</TargetFramework>
					<DisableSourceLink>true</DisableSourceLink>
					{properties}
				</PropertyGroup>
				<ItemGroup>
					<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" />
					{items}
				</ItemGroup>
			</Project>
			""";

	public static string Web(string properties = "", string items = "") =>
		$"""
			<Project Sdk="Microsoft.NET.Sdk.Web">
				<PropertyGroup>
					<TargetFramework>net10.0</TargetFramework>
					<DisableSourceLink>true</DisableSourceLink>
					{properties}
				</PropertyGroup>
				<ItemGroup>
					{items}
				</ItemGroup>
			</Project>
			""";

	public const string ClientProgram = """
		using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

		var builder = WebAssemblyHostBuilder.CreateDefault(args);
		await builder.Build().RunAsync().ConfigureAwait(false);
		""";

	public const string HostProgram = """
		var builder = WebApplication.CreateBuilder(args);
		var app = builder.Build();
		app.UseBlazorFrameworkFiles();
		app.UseStaticFiles();
		await app.RunAsync().ConfigureAwait(false);
		""";

	/// <summary>
	/// Creates a project in a new throwaway solution. The returned harness owns the solution directory, so
	/// disposing it removes every sibling added with <see cref="AddSiblingAsync"/>.
	/// </summary>
	public static Task<ProjectHarness> CreateAsync(
		string projectName,
		string content,
		string namespacePrefix = "Acme",
		CancellationToken cancellationToken = default
	) =>
		ProjectHarness
			.For(projectName)
			.WithNamespacePrefix(namespacePrefix)
			.WithProjectFileContent(content)
			.BuildAsync(cancellationToken);

	/// <summary>Adds a project beside <paramref name="owner"/>, in the same solution.</summary>
	public static Task<ProjectHarness> AddSiblingAsync(
		ProjectHarness owner,
		string projectName,
		string content,
		string namespacePrefix = "Acme",
		CancellationToken cancellationToken = default
	) =>
		ProjectHarness
			.For(projectName)
			.WithNamespacePrefix(namespacePrefix)
			.WithProjectFileContent(content)
			.WithSolutionDirectory(owner.SolutionDirectory)
			.BuildAsync(cancellationToken);

	/// <summary>
	/// Replaces the harness's central package versions with the ones a Blazor Web App restores: the
	/// WebAssembly packages for the TFM the fixtures target, and the automatic Purview telemetry packages.
	/// </summary>
	public static Task WritePackageVersionsAsync(ProjectHarness owner, CancellationToken cancellationToken) =>
		File.WriteAllTextAsync(
			Path.Combine(owner.SolutionDirectory, "Directory.Packages.props"),
			"""
			<Project>
				<PropertyGroup>
					<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
				</PropertyGroup>
				<ItemGroup>
					<PackageVersion Include="Microsoft.SourceLink.GitHub" Version="*" />
					<PackageVersion Include="Purview.Telemetry.SourceGenerator" Version="*" />
					<PackageVersion Include="Microsoft.Extensions.Telemetry.Abstractions" Version="10.*" />
					<PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.*" />
					<PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly.Server" Version="10.0.*" />
				</ItemGroup>
			</Project>
			""",
			cancellationToken
		);

	public static Task WriteSourceAsync(
		ProjectHarness project,
		string fileName,
		string content,
		CancellationToken cancellationToken
	) => File.WriteAllTextAsync(Path.Combine(project.ProjectDirectory, fileName), content, cancellationToken);
}
