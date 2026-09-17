using System.Diagnostics;

namespace Purview.BuildSdk;

public sealed class DefaultsPropsTests
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

			var (exitCode, stdOut, stdErr) = await RunProcessAsync(
				"dotnet",
				$"msbuild \"{testProjectPath}\" -nologo -t:NoOp",
				tempRoot,
				cancellationToken
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

	static async Task<(int Code, string StdOut, string StdErr)> RunProcessAsync(
		string fileName,
		string arguments,
		string workingDirectory,
		CancellationToken cancellationToken
	)
	{
		using var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			},
		};

		process.Start();
		var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return (process.ExitCode, await stdOutTask, await stdErrTask);
	}
}
