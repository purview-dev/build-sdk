namespace Purview.BuildSdk.Infra;

/// <summary>
/// The one place a test spawns a process. Every throwaway project is built, evaluated and packed
/// through here so that no test depends on the machine it happens to run on.
/// </summary>
/// <remarks>
/// A throwaway consumer project must not inherit the CI runner's environment. Variables such as
/// <c>GITHUB_WORKSPACE</c> change where the SDK thinks the repository root is, and <c>CI</c>,
/// <c>GITHUB_ACTIONS</c> and <c>TF_BUILD</c> change which advice it suppresses, so an inheriting test
/// asserts something different in CI than it does locally. One test did exactly that and reported a
/// failure that could not be reproduced off the runner.
///
/// They are removed unless the caller explicitly overrides them, which is how a test simulates a CI
/// environment on purpose. Keeping the list in one place is what stops the next test from quietly
/// needing its own copy.
/// </remarks>
static class ProcessRunner
{
	/// <summary>
	/// Runs <paramref name="fileName"/> with the host CI environment stripped, returning its exit
	/// code, standard output and standard error.
	/// </summary>
	public static Task<(int Code, string StdOut, string StdErr)> RunAsync(
		string fileName,
		string arguments,
		string workingDirectory,
		CancellationToken cancellationToken = default
	) => RunWithEnvironmentAsync(fileName, arguments, workingDirectory, environment: null, cancellationToken);

	/// <summary>
	/// Runs <c>dotnet</c>, which is what almost every call site wants.
	/// </summary>
	public static Task<(int Code, string StdOut, string StdErr)> RunDotNetAsync(
		string arguments,
		string workingDirectory,
		CancellationToken cancellationToken = default
	) => RunAsync("dotnet", arguments, workingDirectory, cancellationToken);

	/// <summary>
	/// As <see cref="RunAsync"/>, but with <paramref name="environment"/> also set on the child process.
	/// The remaining CI variables are still stripped, so a test can opt back in to exactly the ones it
	/// is exercising and no others.
	/// </summary>
	public static async Task<(int Code, string StdOut, string StdErr)> RunWithEnvironmentAsync(
		string fileName,
		string arguments,
		string workingDirectory,
		IReadOnlyDictionary<string, string>? environment,
		CancellationToken cancellationToken = default
	)
	{
		using var process = new System.Diagnostics.Process
		{
			StartInfo = new System.Diagnostics.ProcessStartInfo
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

		if (environment is not null)
		{
			foreach (var (key, value) in environment)
				process.StartInfo.Environment[key] = value;
		}

		IsolateFromHostEnvironment(process.StartInfo.Environment, environment);

		process.Start();
		var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return (process.ExitCode, await stdoutTask, await stderrTask);
	}

	/// <summary>
	/// Removes the CI runner's environment from a child process, unless the caller overrode the
	/// variable on purpose.
	/// </summary>
	internal static void IsolateFromHostEnvironment(
		IDictionary<string, string?> environment,
		IReadOnlyDictionary<string, string>? overrides
	)
	{
		foreach (var name in CiEnvironmentVariables)
		{
			if (overrides is null || !overrides.ContainsKey(name))
				environment.Remove(name);
		}
	}

	static readonly string[] CiEnvironmentVariables =
	[
		"CI",
		"GITHUB_ACTIONS",
		"GITHUB_WORKSPACE",
		"GITHUB_SHA",
		"GITHUB_REF",
		"GITHUB_HEAD_REF",
		"GITHUB_BASE_REF",
		"TF_BUILD",
		"BUILD_SOURCESDIRECTORY",
		"BUILD_REPOSITORY_LOCALPATH",
		"CI_PROJECT_DIR",
		"RUNNER_OS",
		"RUNNER_ARCH",
		"RUNNER_TEMP",
	];
}
