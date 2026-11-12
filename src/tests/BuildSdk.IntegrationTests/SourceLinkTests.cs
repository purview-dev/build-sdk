using Purview.BuildSdk.Harness;

namespace Purview.BuildSdk;

/// <summary>
/// Pins how the SDK wires SourceLink: the default provider, that a repository can select the provider
/// for its own host (Azure DevOps, GitLab, Bitbucket, ...), and that the documented opt-out really
/// removes the package.
/// </summary>
/// <remarks>
/// <c>SourceLinkPackageName</c> and <c>DisableSourceLink</c> are read during <c>Sdk.props</c>, so a
/// repository must set them before the SDK import — these tests use <c>preImportProps</c> for that
/// reason. They evaluate rather than build: the harness declares only
/// <c>Microsoft.SourceLink.GitHub</c> centrally, so a selected provider other than GitHub would fail a
/// restore with <c>NU1010</c>, which is not what is under test here.
/// </remarks>
sealed class SourceLinkTests
{
	const string SourceLinkPrefix = "Microsoft.SourceLink.";

	static string[] SourceLinkReferences(IReadOnlyList<string> packageReferences) =>
		[.. packageReferences.Where(reference => reference.StartsWith(SourceLinkPrefix, StringComparison.Ordinal))];

	[Test]
	public async Task NonTestProject_GetsTheGitHubProviderByDefault(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);

		var eval = await h.EvaluateAsync(
			["DisableSourceLink", "SourceLinkPackageName"],
			["PackageReference"],
			cancellationToken
		);

		await Assert
			.That(eval.Properties["SourceLinkPackageName"])
			.IsEqualTo("Microsoft.SourceLink.GitHub")
			.Because("GitHub is the sensible default provider");
		await Assert.That(eval.Properties["DisableSourceLink"]).IsEqualTo("false");
		await Assert.That(SourceLinkReferences(eval.Items["PackageReference"])).Contains("Microsoft.SourceLink.GitHub");
	}

	/// <summary>
	/// A repository on Azure DevOps, GitLab, Bitbucket or an on-premises server sets
	/// <c>SourceLinkPackageName</c> before the SDK import; it replaces the GitHub default rather than
	/// joining it.
	/// </summary>
	[Test]
	public async Task SelectingAProvider_ReplacesTheGitHubDefault(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<SourceLinkPackageName>Microsoft.SourceLink.AzureRepos.Git</SourceLinkPackageName>",
			cancellationToken: cancellationToken
		);

		var references = SourceLinkReferences(await h.GetItemIdentitiesAsync("PackageReference", cancellationToken));

		await Assert.That(references).Contains("Microsoft.SourceLink.AzureRepos.Git");
		await Assert.That(references).DoesNotContain("Microsoft.SourceLink.GitHub");
	}

	[Test]
	public async Task TestProject_DoesNotGetAnAutomaticSourceLinkReference(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("MyLibrary.UnitTests", cancellationToken: cancellationToken);

		var references = SourceLinkReferences(await h.GetItemIdentitiesAsync("PackageReference", cancellationToken));

		await Assert
			.That(references)
			.IsEmpty()
			.Because("test projects are excluded from the automatic SourceLink package");
	}

	[Test]
	public async Task SharedTestingProject_DoesNotGetAnAutomaticSourceLinkReference(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync("SharedTestingFramework", cancellationToken: cancellationToken);

		var references = SourceLinkReferences(await h.GetItemIdentitiesAsync("PackageReference", cancellationToken));

		await Assert
			.That(references)
			.IsEmpty()
			.Because("shared-testing projects are excluded from the automatic SourceLink package");
	}

	/// <summary>
	/// The documented opt-out. It only works when set before the SDK import — a value in the project
	/// body is evaluated after the package ItemGroup and has no effect.
	/// </summary>
	[Test]
	public async Task DisableSourceLink_RemovesTheAutomaticReference(CancellationToken cancellationToken)
	{
		using var h = await ProjectHarness.CreateAsync(
			"MyLibrary",
			preImportProps: "<DisableSourceLink>true</DisableSourceLink>",
			cancellationToken: cancellationToken
		);

		var eval = await h.EvaluateAsync(["DisableSourceLink"], ["PackageReference"], cancellationToken);

		await Assert.That(eval.Properties["DisableSourceLink"]).IsEqualTo("true");
		await Assert.That(SourceLinkReferences(eval.Items["PackageReference"])).IsEmpty();
	}
}
