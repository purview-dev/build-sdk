using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Purview.BuildSdk.Analyzers.ExtensionsNamespace;

namespace Purview.BuildSdk.Analyzers.Extensions;

/// <summary>
/// Integration tests for <see cref="ExtensionsNamespaceAnalyzer"/> using Roslyn's analyzer
/// testing harness with TUnit.
/// </summary>
[Category("Integration")]
public sealed class ExtensionsNamespaceAnalyzerTests
{
	static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
		string filePath,
		string source,
		CancellationToken cancellationToken
	)
	{
		var compilation = AnalyzerTestInfrastructure.CreateCompilation(source, filePath);
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ExtensionsNamespaceAnalyzer());
		var options = AnalyzerTestInfrastructure.CreateAnalyzerOptions();
		var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, options);
		return await compilationWithAnalyzers.GetAllDiagnosticsAsync(cancellationToken);
	}

	[Test]
	public async Task Analyzer_NoDiagnostic_WhenNamespaceMatchesExtensionsFolder(CancellationToken cancellationToken)
	{
		const string source = """
			namespace System
			{
				public static class StringExtensions { }
			}
			""";

		var diagnostics = await AnalyzeAsync(
			@"C:\FakeProject\Extensions\System\StringExtensions.cs",
			source,
			cancellationToken
		);

		Diagnostic[] matches = [.. diagnostics.Where(d => d.Id == ExtensionsNamespaceAnalyzer.DiagnosticId)];
		await Assert.That(matches.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Analyzer_RaisesDiagnostic_WhenNamespaceDoesNotMatchExtensionsFolder(
		CancellationToken cancellationToken
	)
	{
		const string source = """
			namespace An.Example.Project.Extensions.System
			{
				public static class StringExtensions { }
			}
			""";

		var diagnostics = await AnalyzeAsync(
			@"C:\FakeProject\Extensions\System\StringExtensions.cs",
			source,
			cancellationToken
		);

		Diagnostic[] matches = [.. diagnostics.Where(d => d.Id == ExtensionsNamespaceAnalyzer.DiagnosticId)];
		await Assert.That(matches).HasSingleItem();
		await Assert
			.That(matches[0].GetMessage(CultureInfo.InvariantCulture))
			.Contains("An.Example.Project.Extensions.System");
		await Assert.That(matches[0].GetMessage(CultureInfo.InvariantCulture)).Contains("System");
	}

	[Test]
	[Arguments(
		@"C:\FakeProject\Extensions\Microsoft\Extensions\Configuration\ConfigExt.cs",
		"An.Example.Project.Microsoft.Extensions.Configuration",
		"Microsoft.Extensions.Configuration",
		DisplayName = "Deeply nested path → full path used as namespace"
	)]
	[Arguments(
		@"C:\FakeProject\Extensions\System\StringExtensions.cs",
		"An.Example.Project.Extensions.System",
		"System",
		DisplayName = "Single level → folder name only"
	)]
	public async Task Analyzer_RaisesDiagnostic_WithCorrectExpectedNamespace(
		string fileName,
		string wrongNamespace,
		string expectedNamespace,
		CancellationToken cancellationToken
	)
	{
		var source = $$"""
			namespace {{wrongNamespace}}
			{
				public static class Ext { }
			}
			""";

		var diagnostics = await AnalyzeAsync(fileName, source, cancellationToken);

		Diagnostic[] matches = [.. diagnostics.Where(d => d.Id == ExtensionsNamespaceAnalyzer.DiagnosticId)];
		await Assert.That(matches).HasSingleItem();
		await Assert.That(matches[0].GetMessage(CultureInfo.InvariantCulture)).Contains(wrongNamespace);
		await Assert.That(matches[0].GetMessage(CultureInfo.InvariantCulture)).Contains(expectedNamespace);
	}

	[Test]
	public async Task Analyzer_NoDiagnostic_ForFileAtProjectRoot(CancellationToken cancellationToken)
	{
		const string source = """
			namespace An.Example.Project
			{
				class Program { }
			}
			""";

		var diagnostics = await AnalyzeAsync(@"C:\FakeProject\Program.cs", source, cancellationToken);
		Diagnostic[] matches = [.. diagnostics.Where(d => d.Id == ExtensionsNamespaceAnalyzer.DiagnosticId)];
		await Assert.That(matches.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Analyzer_NoDiagnostic_ForNestedExtensionsFolder(CancellationToken cancellationToken)
	{
		const string source = """
			namespace An.Example.Project.Services.Extensions
			{
				public static class ServiceExtensions { }
			}
			""";

		var diagnostics = await AnalyzeAsync(
			@"C:\FakeProject\Services\Extensions\ServiceExtensions.cs",
			source,
			cancellationToken
		);
		Diagnostic[] matches = [.. diagnostics.Where(d => d.Id == ExtensionsNamespaceAnalyzer.DiagnosticId)];
		await Assert.That(matches.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Analyzer_RaisesDiagnostic_ForFileScopedNamespace(CancellationToken cancellationToken)
	{
		const string source = """
			namespace An.Example.Project.Extensions.System;

			public static class StringExtensions { }
			""";

		var diagnostics = await AnalyzeAsync(
			@"C:\FakeProject\Extensions\System\StringExtensions.cs",
			source,
			cancellationToken
		);

		Diagnostic[] matches = [.. diagnostics.Where(d => d.Id == ExtensionsNamespaceAnalyzer.DiagnosticId)];
		await Assert.That(matches).HasSingleItem();
		await Assert
			.That(matches[0].GetMessage(CultureInfo.InvariantCulture))
			.Contains("An.Example.Project.Extensions.System");
		await Assert.That(matches[0].GetMessage(CultureInfo.InvariantCulture)).Contains("System");
	}

	[Test]
	public async Task Analyzer_NoDiagnostic_ForTopLevelStatements(CancellationToken cancellationToken)
	{
		const string source = """
			using System;
			Console.WriteLine("hello");
			""";

		var diagnostics = await AnalyzeAsync(@"C:\FakeProject\Extensions\System\Program.cs", source, cancellationToken);
		Diagnostic[] matches = [.. diagnostics.Where(d => d.Id == ExtensionsNamespaceAnalyzer.DiagnosticId)];
		await Assert.That(matches.Length).IsEqualTo(0);
	}
}
