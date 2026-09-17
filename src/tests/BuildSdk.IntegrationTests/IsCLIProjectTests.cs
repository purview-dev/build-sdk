using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies that IsCLIProject property is correctly detected based on project naming conventions.
/// Projects ending with CLI, Console, or CommandLine (case-insensitive) should be classified
/// as CLI projects.
/// </summary>
public sealed class IsCLIProjectTests
{
	/// <summary>
	/// Helper to create a project and evaluate its IsCLIProject property.
	/// </summary>
	static async Task<(ProjectHarness Harness, bool IsCLIProject)> CreateProjectAndEvaluateAsync(
		string projectName,
		CancellationToken cancellationToken = default
	)
	{
		var extProps = """
				<OutputType>Library</OutputType>
			""";
		var extraItems = """
				<PackageReference Remove="Purview.Telemetry.SourceGenerator" />
				<PackageReference Remove="Microsoft.Extensions.Telemetry.Abstractions" />
				<PackageReference Remove="Microsoft.SourceLink.GitHub" />
			""";

		var harness = await ProjectHarness.CreateAsync(
			projectName,
			extraProps: extProps,
			extraItems: extraItems,
			cancellationToken: cancellationToken
		);

		var isCLIProjectValue = await harness.GetPropertyAsync("IsCLIProject", cancellationToken);
		var isCLIProject = isCLIProjectValue.Equals("true", StringComparison.OrdinalIgnoreCase);

		return (harness, isCLIProject);
	}

	[Test]
	public async Task ProjectEndingWithCLI_Uppercase_IsCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("MyCLI", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsTrue();
		}
	}

	[Test]
	[Arguments("appsettings.json")]
	[Arguments("appsettings.json", "appsettings.Development.json")]
	[Arguments("appsettings.json", "appsettings.Development.json", "appsettings.Test.json")]
	[Arguments(
		"appsettings.json",
		"appsettings.Development.json",
		"appsettings.Test.json",
		"appsettings.Acceptance.json"
	)]
	[Arguments(
		"appsettings.json",
		"appsettings.Development.json",
		"appsettings.Test.json",
		"appsettings.Acceptance.json",
		"appsettings.Production.json"
	)]
	[Arguments(
		"appsettings.Development.json",
		"appsettings.Test.json",
		"appsettings.Acceptance.json",
		"appsettings.Production.json"
	)]
	[Arguments("appsettings.Test.json", "appsettings.Acceptance.json", "appsettings.Production.json")]
	[Arguments("appsettings.Acceptance.json", "appsettings.Production.json")]
	[Arguments("appsettings.Production.json")]
	public async Task ProjectEndingWithCLI_HasAppSettings_CopiedOnBuild(
		string[] appsettings,
		CancellationToken cancellationToken
	)
	{
		var (harness, _) = await CreateProjectAndEvaluateAsync("MyCLI", cancellationToken);

		for (var i = 0; i < appsettings.Length; i++)
		{
			var appSettingsFile = Path.Combine(harness.ProjectDirectory, appsettings[i]);
			await File.WriteAllTextAsync(appSettingsFile, "{ }", cancellationToken);
		}

		using (harness)
		{
			var (success, output, errors) = await harness.BuildAsync(true, cancellationToken: cancellationToken);
			await Assert.That(success).IsTrue().Because(TestHelpers.GenerateError(output, errors));

			var bin = Path.Combine(harness.ProjectDirectory, "bin");

			await Assert.That(Directory.Exists(bin)).IsTrue();

			var files = Directory
				.GetFiles(bin, "*", SearchOption.AllDirectories)
				.Where(f => appsettings.Contains(Path.GetFileName(f)))
				.ToArray();

			await Assert.That(files.Length).IsEqualTo(appsettings.Length);
		}
	}

	[Test]
	public async Task ProjectCLI_HasNoWarnCA1515(CancellationToken cancellationToken)
	{
		var (harness, _) = await CreateProjectAndEvaluateAsync("MyCLI", cancellationToken);

		using (harness)
		{
			var properties = await harness.GetPropertiesAsync(cancellationToken, "NoWarn");

			await Assert.That(properties).ContainsKey("NoWarn");
			await Assert.That(properties["NoWarn"]).Contains("CA1515");
		}
	}

	[Test]
	public async Task ProjectEndingWithDotCLI_Uppercase_IsCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("My.CLI", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsTrue();
		}
	}

	[Test]
	public async Task ProjectEndingWithCLI_PartOfCompoundName_IsCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("MyAppCLI", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsTrue();
		}
	}

	[Test]
	public async Task ProjectEndingWithConsole_Uppercase_IsCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("MyConsole", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsTrue();
		}
	}

	[Test]
	public async Task ProjectEndingWithConsoleApp_IsCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("MyAppConsole", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsTrue();
		}
	}

	[Test]
	public async Task ProjectEndingWithCommandLine_Uppercase_IsCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("MyCommandLine", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsTrue();
		}
	}

	[Test]
	public async Task ProjectEndingWithCommandLineApp_IsCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("MyAppCommandLine", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsTrue();
		}
	}

	[Test]
	public async Task ProjectEndingWithcli_Lowercase_IsNotCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("Myappcli", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsFalse();
		}
	}

	[Test]
	public async Task ProjectEndingWithconsole_Lowercase_IsNotCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("Myappconsole", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsFalse();
		}
	}

	[Test]
	public async Task ProjectEndingWithcommandline_Lowercase_IsNotCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("Myappcommandline", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsFalse();
		}
	}

	[Test]
	public async Task ProjectEndingWithCommandLIne_MixedCase_IsNotCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("MyappCommandLIne", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsFalse();
		}
	}

	[Test]
	[Arguments("MyLibrary")]
	[Arguments("MyService")]
	[Arguments("CoreAPI")]
	[Arguments("SharedUtilities")]
	[Arguments("DataAccess")]
	public async Task ProjectNotEndingWithCLIMarkers_IsNotCLIProject(
		string projectName,
		CancellationToken cancellationToken
	)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync(projectName, cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsFalse();
		}
	}

	[Test]
	public async Task ProjectWithCLIInMiddle_NotEndingSufficiently_IsNotCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("MyCLITools", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsFalse();
		}
	}

	[Test]
	public async Task ProjectWithConsoleInMiddle_NotEndingSufficiently_IsNotCLIProject(
		CancellationToken cancellationToken
	)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("MyConsoleTool", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsFalse();
		}
	}

	[Test]
	public async Task JustCLI_IsStillCLIProject(CancellationToken cancellationToken)
	{
		var (harness, isCLIProject) = await CreateProjectAndEvaluateAsync("CLI", cancellationToken);

		using (harness)
		{
			await Assert.That(isCLIProject).IsTrue();
		}
	}
}
