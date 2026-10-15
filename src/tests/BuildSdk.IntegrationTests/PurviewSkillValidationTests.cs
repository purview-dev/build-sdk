using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Verifies <c>ValidatePurviewSkills</c>. A malformed skill fails quietly where it hurts most - the
/// skills CLI skips a <c>SKILL.md</c> it cannot parse, so the skill never appears and a consumer
/// cannot tell that from one that was never written. These tests pin each failure mode.
/// </summary>
sealed class PurviewSkillValidationTests
{
	static async Task<ProjectHarness> CreateHarnessAsync(CancellationToken cancellationToken)
	{
		var harness = await ProjectHarness.CreateAsync("SkillValidation", cancellationToken: cancellationToken);
		// AGENTS.md is the repository-root marker PurviewSkillsSourceFolder resolves against.
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Agents",
			cancellationToken
		);
		return harness;
	}

	static async Task WriteSkillAsync(
		ProjectHarness harness,
		string folderName,
		string content,
		CancellationToken cancellationToken
	)
	{
		var skillDirectory = Path.Combine(harness.SolutionDirectory, "skills", folderName);
		Directory.CreateDirectory(skillDirectory);
		await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), content, cancellationToken);
	}

	static string ValidSkill(string name) =>
		$"---\nname: {name}\ndescription: Use when exercising skill validation.\n---\n\n# {name}\n";

	static Task<(int Code, string StdOut, string StdErr)> ValidateAsync(
		ProjectHarness harness,
		CancellationToken cancellationToken
	) => harness.RunMSBuildAsync("-t:ValidatePurviewSkills -v:n", cancellationToken);

	[Test]
	public async Task ValidatePurviewSkills_AcceptsAWellFormedSkill(CancellationToken cancellationToken)
	{
		using var harness = await CreateHarnessAsync(cancellationToken);
		await WriteSkillAsync(harness, "purview-good", ValidSkill("purview-good"), cancellationToken);

		var (exitCode, stdOut, stdErr) = await ValidateAsync(harness, cancellationToken);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
	}

	[Test]
	public async Task ValidatePurviewSkills_AcceptsABlockScalarDescription(CancellationToken cancellationToken)
	{
		using var harness = await CreateHarnessAsync(cancellationToken);
		// The real Purview skills use a '>-' block for the description, so this shape must pass.
		await WriteSkillAsync(
			harness,
			"purview-block",
			"---\nname: purview-block\ndescription: >-\n  Use when the description spans\n  several lines.\n---\n\n# Block\n",
			cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await ValidateAsync(harness, cancellationToken);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
	}

	[Test]
	public async Task ValidatePurviewSkills_RejectsAFolderWithNoSkillFile(CancellationToken cancellationToken)
	{
		using var harness = await CreateHarnessAsync(cancellationToken);
		Directory.CreateDirectory(Path.Combine(harness.SolutionDirectory, "skills", "purview-empty"));

		var (exitCode, stdOut, stdErr) = await ValidateAsync(harness, cancellationToken);

		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut + stdErr).Contains("no SKILL.md");
	}

	[Test]
	public async Task ValidatePurviewSkills_RejectsMissingFrontmatter(CancellationToken cancellationToken)
	{
		using var harness = await CreateHarnessAsync(cancellationToken);
		await WriteSkillAsync(harness, "purview-bare", "# Just a heading\n", cancellationToken);

		var (exitCode, stdOut, stdErr) = await ValidateAsync(harness, cancellationToken);

		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut + stdErr).Contains("does not open with YAML frontmatter");
	}

	[Test]
	public async Task ValidatePurviewSkills_RejectsAnEmptyDescription(CancellationToken cancellationToken)
	{
		using var harness = await CreateHarnessAsync(cancellationToken);
		await WriteSkillAsync(
			harness,
			"purview-nodesc",
			"---\nname: purview-nodesc\ndescription:\n---\n\n# No description\n",
			cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await ValidateAsync(harness, cancellationToken);

		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut + stdErr).Contains("activation trigger");
	}

	[Test]
	public async Task ValidatePurviewSkills_RejectsANameThatDoesNotMatchItsFolder(CancellationToken cancellationToken)
	{
		using var harness = await CreateHarnessAsync(cancellationToken);
		// The CLI installs by frontmatter name while a human reads the folder; a mismatch means the
		// skill installs under a name nobody expects.
		await WriteSkillAsync(harness, "purview-folder", ValidSkill("purview-something-else"), cancellationToken);

		var (exitCode, stdOut, stdErr) = await ValidateAsync(harness, cancellationToken);

		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut + stdErr).Contains("but the folder is");
	}

	[Test]
	public async Task ValidatePurviewSkills_RejectsAMissingReferenceFile(CancellationToken cancellationToken)
	{
		using var harness = await CreateHarnessAsync(cancellationToken);
		await WriteSkillAsync(
			harness,
			"purview-refs",
			"---\nname: purview-refs\ndescription: Use when checking references.\n---\n\nSee `references/missing.md`.\n",
			cancellationToken
		);

		var (exitCode, stdOut, stdErr) = await ValidateAsync(harness, cancellationToken);

		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut + stdErr).Contains("references/missing.md");
	}

	[Test]
	public async Task ValidatePurviewSkills_AcceptsAReferenceFileThatExists(CancellationToken cancellationToken)
	{
		using var harness = await CreateHarnessAsync(cancellationToken);
		await WriteSkillAsync(
			harness,
			"purview-refs-ok",
			"---\nname: purview-refs-ok\ndescription: Use when checking references.\n---\n\nSee `references/detail.md`.\n",
			cancellationToken
		);
		var referencesDirectory = Path.Combine(harness.SolutionDirectory, "skills", "purview-refs-ok", "references");
		Directory.CreateDirectory(referencesDirectory);
		await File.WriteAllTextAsync(Path.Combine(referencesDirectory, "detail.md"), "# Detail", cancellationToken);

		var (exitCode, stdOut, stdErr) = await ValidateAsync(harness, cancellationToken);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
	}

	[Test]
	public async Task ValidatePurviewSkills_OnlyChecksTheSkillsThisProjectPacks(CancellationToken cancellationToken)
	{
		using var harness = await CreateHarnessAsync(cancellationToken);
		await WriteSkillAsync(harness, "purview-shipped", ValidSkill("purview-shipped"), cancellationToken);
		// Broken, but owned by a different project in the same repository.
		await WriteSkillAsync(harness, "purview-other", "# no frontmatter\n", cancellationToken);

		var (exitCode, stdOut, stdErr) = await harness.RunMSBuildAsync(
			"-t:ValidatePurviewSkills -v:n -p:PurviewSkillsToPack=purview-shipped",
			cancellationToken
		);

		await Assert.That(exitCode).IsZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
	}

	[Test]
	public async Task Pack_FailsWhenAPackedSkillIsMalformed(CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync(
			"SkillValidationPack",
			extraProps: """
			<IsPackable>true</IsPackable>
			<DisableSourceLink>true</DisableSourceLink>
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>
			<PurviewPackRepositorySkills>true</PurviewPackRepositorySkills>
			""",
			cancellationToken: cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Agents",
			cancellationToken
		);
		await WriteSkillAsync(harness, "purview-broken", "# no frontmatter\n", cancellationToken);

		var feedDirectory = Path.Combine(harness.SolutionDirectory, "feed");
		var (exitCode, stdOut, stdErr) = await RunProcessAsync(
			"dotnet",
			$"pack \"{harness.ProjectFilePath}\" -c Release -o \"{feedDirectory}\" -p:PackageVersion=0.0.0-test -p:Version=0.0.0-test",
			harness.SolutionDirectory,
			cancellationToken
		);

		// The repository that owns the skill is the one that sees the failure, rather than shipping
		// a skill the CLI will silently skip.
		await Assert.That(exitCode).IsNotEqualTo(0);
		await Assert.That(stdOut + stdErr).Contains("PRSKL0005");
	}

	static async Task<(int Code, string StdOut, string StdErr)> RunProcessAsync(
		string fileName,
		string arguments,
		string workingDirectory,
		CancellationToken cancellationToken
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

		process.Start();
		var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return (process.ExitCode, await stdoutTask, await stderrTask);
	}
}
