using Purview.BuildSdk.Harness;
using Purview.BuildSdk.Infra;

namespace Purview.BuildSdk;

/// <summary>
/// Pins the incrementality contract for the files this SDK generates into every project.
/// </summary>
/// <remarks>
/// Both generated files are added to <c>@(Compile)</c>, which is one of <c>CoreCompile</c>'s
/// <c>Inputs</c>. Writing either of them unconditionally gives it a fresh timestamp, so the compiler
/// output is never newer than its inputs and a no-change rebuild recompiles the project. That is not
/// a theory: it was measured on a 39-project solution, where a rebuild with no edits ran 38
/// <c>Csc</c> invocations totalling 111 s of a 68 s build, and the fix was a single attribute on each
/// writer. These tests exist so the attribute cannot quietly go missing again.
/// </remarks>
sealed class IncrementalBuildTests
{
	const string AssemblyInfoFileName = "GeneratedAssemblyInfo.cs";
	const string AssemblyIdentifierFileName = "GeneratedAssemblyIdentifier.cs";

	[Test]
	public async Task AssemblyInfoClass_IsNotRewrittenWhenItsContentHasNotChanged(CancellationToken cancellationToken)
	{
		await AssertGeneratedFileIsStable(AssemblyInfoFileName, cancellationToken);
	}

	[Test]
	public async Task AssemblyIdentifierClass_IsNotRewrittenWhenItsContentHasNotChanged(
		CancellationToken cancellationToken
	)
	{
		await AssertGeneratedFileIsStable(AssemblyIdentifierFileName, cancellationToken);
	}

	/// <summary>
	/// Builds the same unchanged project twice and requires the generated file's timestamp to survive.
	/// A rewrite is what breaks incrementality, and a rewrite always moves the timestamp - so the
	/// timestamp is a more direct assertion than counting compiler invocations.
	/// </summary>
	static async Task AssertGeneratedFileIsStable(string fileName, CancellationToken cancellationToken)
	{
		using var harness = await ProjectHarness.CreateAsync(
			"IncrementalConsumer",
			// The telemetry packages the SDK adds by default are not part of this scenario and would
			// need Central Package Management versions the throwaway project does not declare.
			extraProps: """
			<ExcludePurviewTelemetry>true</ExcludePurviewTelemetry>
			<ExcludeMSTelemetryExtension>true</ExcludeMSTelemetryExtension>
			""",
			cancellationToken: cancellationToken
		);

		var (successFirst, outputFirst, errorsFirst) = await harness.BuildAsync(
			restore: true,
			cancellationToken: cancellationToken
		);
		await Assert.That(successFirst).IsTrue().Because(TestHelpers.GenerateError(outputFirst, errorsFirst));

		var generatedPath = FindGeneratedFile(harness, fileName);
		var generatedContent = await File.ReadAllTextAsync(generatedPath, cancellationToken);
		var stampedAt = File.GetLastWriteTimeUtc(generatedPath);

		// Long enough that a rewrite would land on a visibly different timestamp, short enough not
		// to slow the suite down. Filesystems with coarse timestamps are exactly what this guards
		// against, so the delay cannot be removed.
		await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

		var (successSecond, outputSecond, errorsSecond) = await harness.BuildAsync(
			cancellationToken: cancellationToken
		);
		await Assert.That(successSecond).IsTrue().Because(TestHelpers.GenerateError(outputSecond, errorsSecond));

		await Assert
			.That(File.GetLastWriteTimeUtc(generatedPath))
			.IsEqualTo(stampedAt)
			.Because(
				$"'{fileName}' is a compile input, so rewriting it when nothing changed recompiles the "
					+ "project on every build - WriteOnlyWhenDifferent=\"true\" is what prevents that"
			);

		// The content must still be the real thing: an unconditional *skip* would satisfy the
		// timestamp assertion while generating nothing.
		await Assert.That(await File.ReadAllTextAsync(generatedPath, cancellationToken)).IsEqualTo(generatedContent);
	}

	static string FindGeneratedFile(ProjectHarness harness, string fileName)
	{
		var matches = Directory.GetFiles(
			Path.Combine(harness.ProjectDirectory, "obj"),
			fileName,
			SearchOption.AllDirectories
		);

		return matches.Length == 1
			? matches[0]
			: throw new InvalidOperationException(
				$"Expected exactly one '{fileName}' beneath '{harness.ProjectDirectory}/obj', found {matches.Length}."
			);
	}
}
