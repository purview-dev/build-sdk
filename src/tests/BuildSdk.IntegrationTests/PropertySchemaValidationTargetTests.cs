using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;
using Purview.BuildSdk.PropertySchema;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies <c>ValidatePurviewPropertySchema</c>, the opt-in check another package runs to keep its
/// <c>.buildschema.json</c> sidecars and its shipped <c>.props</c>/<c>.targets</c> in step.
/// </summary>
sealed class PropertySchemaValidationTargetTests
{
	const string KitProps = """
		<Project>
			<PropertyGroup>
				<AcmeKitEnabled Condition="'$(AcmeKitEnabled)' == ''">true</AcmeKitEnabled>
			</PropertyGroup>
			<ItemGroup>
				<CompilerVisibleProperty Include="AcmeKitEnabled" />
			</ItemGroup>
		</Project>
		""";

	const string KitSchema = """
		{
			"properties": {
				"AcmeKitEnabled": { "description": "Turns the kit on.", "type": "bool", "defaultValue": "true" }
			}
		}
		""";

	static async Task<ProjectHarness> CreatePackageAsync(
		IReadOnlyDictionary<string, string> files,
		CancellationToken cancellationToken,
		string? preImportProperties = null
	)
	{
		// The fixture's build/ files are not named for the package id, which NuGet reports as NU5129. That is
		// not what these tests are about, so the warning must not fail the pack.
		var builder = ProjectHarness
			.For("Kit")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
					</PropertyGroup>
				</Project>
				"""
			);
		if (preImportProperties is not null)
			builder.WithPreImportPropertiesRaw(preImportProperties);

		var h = await builder.BuildAsync(cancellationToken);
		foreach (var (relativePath, content) in files)
		{
			var path = Path.Combine(h.ProjectDirectory, "Sdk", relativePath.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			await File.WriteAllTextAsync(path, content, cancellationToken);
		}

		return h;
	}

	[Test]
	public async Task ValidatePurviewPropertySchema_SidecarAgreesWithTheProps_Succeeds(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var h = await CreatePackageAsync(
			new Dictionary<string, string>
			{
				["buildTransitive/Acme.Kit.props"] = KitProps,
				["buildTransitive/Acme.Kit.props.buildschema.json"] = KitSchema,
			},
			cancellationToken
		);

		// Act
		var (exitCode, stdOut, stdErr) = await h.RunMSBuildAsync("-t:ValidatePurviewPropertySchema", cancellationToken);

		// Assert
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut + stdErr).DoesNotContain("PRPSC");
	}

	[Test]
	public async Task ValidatePurviewPropertySchema_CompilerVisiblePropertyWithoutAnEntry_ReportsPRPSC0001(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var h = await CreatePackageAsync(
			new Dictionary<string, string>
			{
				["buildTransitive/Acme.Kit.props"] = KitProps.Replace(
					"<CompilerVisibleProperty Include=\"AcmeKitEnabled\" />",
					"<CompilerVisibleProperty Include=\"AcmeKitEnabled\" /><CompilerVisibleProperty Include=\"AcmeKitMode\" />",
					StringComparison.Ordinal
				),
				["buildTransitive/Acme.Kit.props.buildschema.json"] = KitSchema,
			},
			cancellationToken
		);

		// Act
		var (exitCode, stdOut, _) = await h.RunMSBuildAsync("-t:ValidatePurviewPropertySchema", cancellationToken);

		// Assert
		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut).Contains("PRPSC0001");
		await Assert.That(stdOut).Contains("'AcmeKitMode' is exported as a CompilerVisibleProperty");
		await Assert.That(stdOut).Contains("Acme.Kit.props.buildschema.json");
	}

	[Test]
	public async Task ValidatePurviewPropertySchema_EntryNothingDefines_ReportsPRPSC0002(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var h = await CreatePackageAsync(
			new Dictionary<string, string>
			{
				["buildTransitive/Acme.Kit.props"] = KitProps,
				["buildTransitive/Acme.Kit.props.buildschema.json"] = KitSchema.Replace(
					"\"AcmeKitEnabled\":",
					"\"AcmeKitRemoved\": \"Gone.\", \"AcmeKitEnabled\":",
					StringComparison.Ordinal
				),
			},
			cancellationToken
		);

		// Act
		var (exitCode, stdOut, _) = await h.RunMSBuildAsync("-t:ValidatePurviewPropertySchema", cancellationToken);

		// Assert
		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut).Contains("PRPSC0002");
		await Assert.That(stdOut).Contains("'AcmeKitRemoved'");
	}

	[Test]
	[Arguments("{ \"properties\": { \"AcmeKitEnabled\": ")]
	[Arguments("{ \"types\": {} }")]
	[Arguments("[]")]
	public async Task ValidatePurviewPropertySchema_UnreadableSidecar_ReportsPRPSC0003(
		string sidecar,
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var h = await CreatePackageAsync(
			new Dictionary<string, string>
			{
				["buildTransitive/Acme.Kit.props"] = KitProps,
				["buildTransitive/Acme.Kit.props.buildschema.json"] = sidecar,
			},
			cancellationToken
		);

		// Act
		var (exitCode, stdOut, _) = await h.RunMSBuildAsync("-t:ValidatePurviewPropertySchema", cancellationToken);

		// Assert
		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut).Contains("PRPSC0003");
	}

	/// <summary>
	/// The most likely placement mistake: naming the sidecar after the package rather than after the
	/// file it describes, or dropping the file's extension, so MSBuild Editor never finds it.
	/// </summary>
	[Test]
	public async Task ValidatePurviewPropertySchema_SidecarWithoutItsFile_ReportsPRPSC0004(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var h = await CreatePackageAsync(
			new Dictionary<string, string>
			{
				["buildTransitive/Acme.Kit.props"] = KitProps,
				["buildTransitive/Acme.Kit.buildschema.json"] = KitSchema,
			},
			cancellationToken
		);

		// Act
		var (exitCode, stdOut, _) = await h.RunMSBuildAsync("-t:ValidatePurviewPropertySchema", cancellationToken);

		// Assert
		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut).Contains("PRPSC0004");
	}

	[Test]
	public async Task ValidatePurviewPropertySchema_IgnoredExport_IsNotReported(CancellationToken cancellationToken)
	{
		// Arrange
		using var h = await CreatePackageAsync(
			new Dictionary<string, string>
			{
				["buildTransitive/Acme.Kit.props"] = KitProps.Replace(
					"<CompilerVisibleProperty Include=\"AcmeKitEnabled\" />",
					"<CompilerVisibleProperty Include=\"AcmeKitEnabled\" /><CompilerVisibleProperty Include=\"RootNamespace\" />",
					StringComparison.Ordinal
				),
				["buildTransitive/Acme.Kit.props.buildschema.json"] = KitSchema,
			},
			cancellationToken,
			preImportProperties: "<PurviewPropertySchemaIgnoredProperties>RootNamespace</PurviewPropertySchemaIgnoredProperties>"
		);

		// Act
		var (exitCode, stdOut, stdErr) = await h.RunMSBuildAsync("-t:ValidatePurviewPropertySchema", cancellationToken);

		// Assert
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
	}

	/// <summary>
	/// Dogfoods the target on Purview.BuildSdk's own Sdk/ folder, ignoring the exports MSBuild and the .NET
	/// SDK own - the same exclusions PropertySchemaTests applies. The two checks therefore cannot drift.
	/// </summary>
	[Test]
	public async Task ValidatePurviewPropertySchema_ThisSdk_Succeeds(CancellationToken cancellationToken)
	{
		// Arrange
		using var h = await ProjectHarness.CreateAsync("Kit", cancellationToken: cancellationToken);
		var ignored = string.Join("%3B", SdkPropertyClassification.ExternallyOwned.Keys);
		var sdkDirectory = SdkPaths.SdkDirectory;

		// Act
		var (exitCode, stdOut, stdErr) = await h.RunMSBuildAsync(
			$"-t:ValidatePurviewPropertySchema \"-p:PurviewPropertySchemaDirectory={sdkDirectory}\" \"-p:PurviewPropertySchemaIgnoredProperties={ignored}\"",
			cancellationToken
		);

		// Assert
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut + stdErr).DoesNotContain("PRPSC");
	}

	/// <summary>
	/// The guarantee every consumer relies on: a package with a broken sidecar still builds and packs
	/// exactly as before unless it opted in.
	/// </summary>
	[Test]
	public async Task Pack_WithoutTheOptIn_DoesNotValidateTheSchema(CancellationToken cancellationToken)
	{
		// Arrange
		using var h = await CreatePackageAsync(
			new Dictionary<string, string>
			{
				["buildTransitive/Acme.Kit.props"] = KitProps,
				["buildTransitive/Acme.Kit.buildschema.json"] = "not json",
			},
			cancellationToken,
			preImportProperties: """
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<DisableSourceLink>true</DisableSourceLink>
			<IsPackable>true</IsPackable>
			"""
		);

		// Act
		var (exitCode, stdOut, stdErr) = await h.RunMSBuildAsync("-restore -t:Build;Pack", cancellationToken);

		// Assert
		await Assert.That(exitCode).IsEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut + stdErr).DoesNotContain("PRPSC");
	}

	[Test]
	public async Task Pack_WithTheOptIn_FailsOnABrokenSidecar(CancellationToken cancellationToken)
	{
		// Arrange
		using var h = await CreatePackageAsync(
			new Dictionary<string, string>
			{
				["buildTransitive/Acme.Kit.props"] = KitProps,
				["buildTransitive/Acme.Kit.buildschema.json"] = KitSchema,
			},
			cancellationToken,
			preImportProperties: """
			<EnablePurviewPropertySchemaValidation>true</EnablePurviewPropertySchemaValidation>
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<DisableSourceLink>true</DisableSourceLink>
			<IsPackable>true</IsPackable>
			"""
		);

		// Act
		var (exitCode, stdOut, _) = await h.RunMSBuildAsync("-restore -t:Pack", cancellationToken);

		// Assert
		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut).Contains("PRPSC0004");
	}
}
