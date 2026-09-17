using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Purview.BuildSdk.Analyzers.ExtensionsNamespace;
using Purview.BuildSdk.CodeFixers.ExtensionsNamespace;

namespace Purview.BuildSdk.Analyzers.IntegrationTests.Extensions;

/// <summary>
/// Integration tests for <see cref="ExtensionsNamespaceCodeFixProvider"/>.
/// </summary>
[Category("Integration")]
public sealed class ExtensionsNamespaceCodeFixTests
{
	static async Task<string> ApplyCodeFixAsync(string fileName, string source, CancellationToken cancellationToken)
	{
		var (workspace, document) = CreateWorkspaceAndDocument(fileName, source);
		try
		{
			document = AddAnalyzerConfigToDocument(document);
			var diagnostic = await RunAnalyzersAndGetDiagnosticAsync(document, cancellationToken);
			var fixedText = await ApplyCodeFixAndGetTextAsync(document, diagnostic, cancellationToken);
			return fixedText;
		}
		finally
		{
			workspace.Dispose();
		}
	}

	static (AdhocWorkspace workspace, Document document) CreateWorkspaceAndDocument(string fileName, string source)
	{
		var workspace = new AdhocWorkspace();
		var projectId = ProjectId.CreateNewId();
		var documentId = DocumentId.CreateNewId(projectId);

		var projectInfo = ProjectInfo
			.Create(
				projectId,
				VersionStamp.Create(),
				"TestProject",
				"TestProject",
				LanguageNames.CSharp,
				parseOptions: CSharpParseOptions.Default,
				compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
			)
			.WithMetadataReferences(AnalyzerTestInfrastructure.BuildBclReferences());

		var nativeFilePath = AnalyzerTestInfrastructure.NormalizeFakePath(fileName);
		var solution = workspace
			.CurrentSolution.AddProject(projectInfo)
			.AddDocument(
				documentId,
				Path.GetFileName(nativeFilePath),
				SourceText.From(source),
				filePath: nativeFilePath
			);

		var document = solution.GetDocument(documentId)!;
		return (workspace, document);
	}

	static Document AddAnalyzerConfigToDocument(Document document)
	{
		var nativeProjectDir = AnalyzerTestInfrastructure.NormalizeFakePath(@"C:\FakeProject\");
		var analyzerConfig = $$"""
			is_root = true
			[*.cs]
			build_property.ProjectDir = {{nativeProjectDir}}
			build_property.RootNamespace = An.Example.Project
			""";

		var solution = document.Project.Solution.AddAnalyzerConfigDocument(
			DocumentId.CreateNewId(document.Project.Id),
			".editorconfig",
			SourceText.From(analyzerConfig),
			filePath: AnalyzerTestInfrastructure.NormalizeFakePath(@"C:\FakeProject\.editorconfig")
		);

		return solution.GetDocument(document.Id)!;
	}

	static async Task<Diagnostic> RunAnalyzersAndGetDiagnosticAsync(
		Document document,
		CancellationToken cancellationToken
	)
	{
		var project = document.Project;
		var compilation = (await project.GetCompilationAsync(cancellationToken))!;

		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ExtensionsNamespaceAnalyzer());
		var diagnostics = await compilation
			.WithAnalyzers(analyzers, project.AnalyzerOptions)
			.GetAnalyzerDiagnosticsAsync(cancellationToken);

		return diagnostics.Single(d => d.Id == ExtensionsNamespaceAnalyzer.DiagnosticId);
	}

	static async Task<string> ApplyCodeFixAndGetTextAsync(
		Document document,
		Diagnostic diagnostic,
		CancellationToken cancellationToken
	)
	{
		var provider = new ExtensionsNamespaceCodeFixProvider();
		var actions = new List<CodeAction>();

		var context = new CodeFixContext(
			document,
			diagnostic,
			(action, _) => actions.Add(action),
			CancellationToken.None
		);

		await provider.RegisterCodeFixesAsync(context);
		var actionToApply = actions.Single();
		var operations = await actionToApply.GetOperationsAsync(CancellationToken.None);

		var applyChanges = operations.OfType<ApplyChangesOperation>().Single();
		var fixedDocument = applyChanges.ChangedSolution.GetDocument(document.Id)!;
		var fixedText = (await fixedDocument.GetTextAsync(cancellationToken))!;
		return fixedText.ToString();
	}

	[Test]
	public async Task CodeFixProvider_IsExported_ForVisualStudioDiscovery(CancellationToken cancellationToken)
	{
		_ = cancellationToken;

		await Assert.That(typeof(ExtensionsNamespaceAnalyzer).IsPublic).IsTrue();
		await Assert.That(typeof(ExtensionsNamespaceSuppressor).IsPublic).IsTrue();
		await Assert.That(typeof(ExtensionsNamespaceCodeFixProvider).IsPublic).IsTrue();

		var attributes = Attribute.GetCustomAttributes(typeof(ExtensionsNamespaceCodeFixProvider), inherit: false);
		await Assert.That(attributes.OfType<ExportCodeFixProviderAttribute>().Any()).IsTrue();
		await Assert.That(attributes.OfType<SharedAttribute>().Any()).IsTrue();
	}

	[Test]
	public async Task FixableDiagnosticIds_ContainsExtensionsNamespaceDiagnosticId(CancellationToken cancellationToken)
	{
		_ = cancellationToken;

		var provider = new ExtensionsNamespaceCodeFixProvider();

		await Assert.That(provider.FixableDiagnosticIds).Contains(ExtensionsNamespaceAnalyzer.DiagnosticId);
	}

	[Test]
	[Arguments(
		@"C:\FakeProject\Extensions\System\StringExtensions.cs",
		"An.Example.Project.Extensions.System",
		"System",
		DisplayName = "Block namespace → corrected to System"
	)]
	[Arguments(
		@"C:\FakeProject\Extensions\Microsoft\Extensions\Configuration\ConfigExt.cs",
		"An.Example.Project.Microsoft.Extensions.Configuration",
		"Microsoft.Extensions.Configuration",
		DisplayName = "Deeply nested → corrected to Microsoft.Extensions.Configuration"
	)]
	public async Task CodeFix_ReplacesNamespace_WithFolderDerivedNamespace(
		string fileName,
		string wrongNamespace,
		string correctNamespace,
		CancellationToken cancellationToken
	)
	{
		var before = $$"""
			namespace {{wrongNamespace}}
			{
				public static class Ext { }
			}
			""";

		var after = $$"""
			namespace {{correctNamespace}}
			{
				public static class Ext { }
			}
			""";

		var fixedSource = await ApplyCodeFixAsync(fileName, before, cancellationToken);
		await Assert.That(fixedSource).IsEqualTo(after);
	}

	[Test]
	public async Task CodeFix_CorrectlyHandles_FileScopedNamespace(CancellationToken cancellationToken)
	{
		const string before = """
			namespace An.Example.Project.Extensions.System;

			public static class StringExtensions { }
			""";

		const string after = """
			namespace System;

			public static class StringExtensions { }
			""";

		var fixedSource = await ApplyCodeFixAsync(
			@"C:\FakeProject\Extensions\System\StringExtensions.cs",
			before,
			cancellationToken
		);
		await Assert.That(fixedSource).IsEqualTo(after);
	}
}
