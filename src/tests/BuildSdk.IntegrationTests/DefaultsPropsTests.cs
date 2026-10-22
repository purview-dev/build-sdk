using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

sealed class DefaultsPropsTests
{
	[Test]
	public async Task NonCsprojEvaluation_DoesNotFailOnBooleanConditions(CancellationToken cancellationToken)
	{
		var tempRoot = Path.Combine(Path.GetTempPath(), "PurviewSdkTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempRoot);

		try
		{
			var directoryBuildPropsPath = Path.Combine(tempRoot, "Directory.Build.props");
			var directoryBuildTargetsPath = Path.Combine(tempRoot, "Directory.Build.targets");
			var testProjectPath = Path.Combine(tempRoot, "restore.proj");

			await File.WriteAllTextAsync(
				directoryBuildPropsPath,
				$$"""
				<Project>
					<PropertyGroup>
						<NamespacePrefix>Test</NamespacePrefix>
					</PropertyGroup>
					<Import Project="{{SdkPaths.SdkDirectory}}/Sdk.props" />
					<PropertyGroup Condition="$(IsTestProject) OR $(IsSharedTestingProject)">
						<NoWarn>$(NoWarn);CA1031;CA2234</NoWarn>
					</PropertyGroup>
				</Project>
				""",
				cancellationToken
			);

			await File.WriteAllTextAsync(
				directoryBuildTargetsPath,
				$$"""
				<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
					<Import Project="{{SdkPaths.SdkDirectory}}/Sdk.targets" />
				</Project>
				""",
				cancellationToken
			);

			await File.WriteAllTextAsync(
				testProjectPath,
				"""
				<Project>
					<Target Name="NoOp" />
				</Project>
				""",
				cancellationToken
			);

			var (exitCode, stdOut, stdErr) = await ProcessRunner.RunDotNetAsync(
				$"msbuild \"{testProjectPath}\" -nologo -t:NoOp",
				tempRoot,
				cancellationToken: cancellationToken
			);

			var output = stdOut + stdErr;
			await Assert.That(exitCode).IsEqualTo(0);
			await Assert.That(output).DoesNotContain("MSB4100");
		}
		finally
		{
			if (Directory.Exists(tempRoot))
				Directory.Delete(tempRoot, recursive: true);
		}
	}
}
