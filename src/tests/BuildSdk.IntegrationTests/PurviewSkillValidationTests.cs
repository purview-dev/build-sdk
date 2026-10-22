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
	public async Task ValidatePurviewSkills_ChecksEveryAuthoredSkill(CancellationToken cancellationToken)
	{
		using var harness = await CreateHarnessAsync(cancellationToken);
		await WriteSkillAsync(harness, "purview-shipped", ValidSkill("purview-shipped"), cancellationToken);
		// Nothing is packed any more, so there is no per-project subset to narrow to: every skill in
		// the repository is published through the CLI, so every skill has to be well formed.
		await WriteSkillAsync(harness, "purview-other", "# no frontmatter\n", cancellationToken);

		var (exitCode, stdOut, stdErr) = await ValidateAsync(harness, cancellationToken);

		await Assert.That(exitCode).IsNotZero().Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut + stdErr).Contains("purview-other");
	}

	[Test]
	public async Task Pack_FailsWhenAPackedSkillIsMalformed(CancellationToken cancellationToken)
	{
		using var harness = await CreatePackHarnessAsync(cancellationToken);
		await WriteSkillAsync(harness, "purview-broken", "# no frontmatter\n", cancellationToken);

		var (exitCode, stdOut, stdErr) = await PackAsync(harness, cancellationToken);

		// The repository that owns the skill is the one that sees the failure, rather than shipping
		// a skill the CLI will silently skip.
		await Assert.That(exitCode).IsNotEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut + stdErr).Contains("PRSKL0005");
	}

	/// <summary>
	/// A CI runner exports <c>GITHUB_WORKSPACE</c>, and the SDK's version detection reads
	/// <c>RepoRoot</c> from it. The throwaway project is not inside that workspace, so inheriting the
	/// variable must not make pack validate the *checkout's* skills while the project's own malformed
	/// one goes unnoticed. This is how the test above first failed on the Linux runner.
	/// </summary>
	[Test]
	public async Task Pack_ValidatesTheProjectsOwnSkills_EvenWhenAGitHubWorkspaceIsInherited(
		CancellationToken cancellationToken
	)
	{
		using var harness = await CreatePackHarnessAsync(
			cancellationToken,
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["GITHUB_WORKSPACE"] = RepositoryRoot }
		);
		await WriteSkillAsync(harness, "purview-broken", "# no frontmatter\n", cancellationToken);

		var (exitCode, stdOut, stdErr) = await PackAsync(harness, cancellationToken);

		await Assert.That(exitCode).IsNotEqualTo(0).Because(TestHelpers.GenerateError(stdOut, stdErr));
		await Assert.That(stdOut + stdErr).Contains("PRSKL0005");
		await Assert.That(stdOut + stdErr).Contains("purview-broken");
	}

	static async Task<ProjectHarness> CreatePackHarnessAsync(
		CancellationToken cancellationToken,
		IDictionary<string, string>? extraEnv = null
	)
	{
		var harness = await ProjectHarness.CreateAsync(
			"SkillValidationPack",
			extraProps: """
			<IsPackable>true</IsPackable>
			<DisableSourceLink>true</DisableSourceLink>
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>
			""",
			extraEnv: extraEnv,
			cancellationToken: cancellationToken
		);
		await File.WriteAllTextAsync(
			Path.Combine(harness.SolutionDirectory, "AGENTS.md"),
			"# Agents",
			cancellationToken
		);
		return harness;
	}

	/// <summary>
	/// Packs through the harness so the runner's environment cannot leak in: a spawned process that
	/// inherits <c>GITHUB_WORKSPACE</c> resolves the wrong repository root and validates the wrong
	/// skills.
	/// </summary>
	static Task<(int Code, string StdOut, string StdErr)> PackAsync(
		ProjectHarness harness,
		CancellationToken cancellationToken
	)
	{
		var feedDirectory = Path.Combine(harness.SolutionDirectory, "feed");

		return harness.RunProcessAsync(
			"dotnet",
			$"pack \"{harness.ProjectFilePath}\" -c Release -o \"{feedDirectory}\" -p:PackageVersion=0.0.0-test -p:Version=0.0.0-test",
			harness.SolutionDirectory,
			cancellationToken
		);
	}

	/// <summary>
	/// This repository's root, found by the same <c>AGENTS.md</c> marker the SDK uses, so a test can
	/// point <c>GITHUB_WORKSPACE</c> at a real checkout.
	/// </summary>
	static string RepositoryRoot
	{
		get
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory is not null)
			{
				if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
					return directory.FullName;

				directory = directory.Parent;
			}

			throw new InvalidOperationException(
				$"No AGENTS.md was found above '{AppContext.BaseDirectory}', so the repository root could not be resolved."
			);
		}
	}
}
