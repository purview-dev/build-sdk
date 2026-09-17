using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Purview.BuildSdk.CodeFixers.TargetTypedObjectCreation;

namespace Purview.BuildSdk.Analyzers.TargetTypedObjectCreation;

public sealed class TargetTypedObjectCreationTests
{
	[Test]
	public async Task Analyzer_MethodCallResult_DoesNotReportDiagnostic(CancellationToken cancellationToken)
	{
		// Arrange
		const string source = "var value = Factory.Create();";
		var document = CreateDocument(source);

		// Act
		var diagnostics = await GetDiagnosticsAsync(document, cancellationToken);

		// Assert
		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	public async Task Analyzer_ObjectCreationWithVar_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		// Arrange
		const string source = "var value = new Widget();";
		var document = CreateDocument(source);

		// Act
		var diagnostics = await GetDiagnosticsAsync(document, cancellationToken);

		// Assert
		await Assert.That(diagnostics).Count().IsEqualTo(1);
		await Assert.That(diagnostics[0].Id).IsEqualTo(TargetTypedObjectCreationAnalyzer.DiagnosticId);
	}

	[Test]
	public async Task CodeFixProvider_Exposes_TargetTypedObjectCreationDiagnosticId(CancellationToken cancellationToken)
	{
		_ = cancellationToken;

		var provider = new TargetTypedObjectCreationCodeFixProvider();

		await Assert.That(provider.FixableDiagnosticIds).Contains(TargetTypedObjectCreationAnalyzer.DiagnosticId);
	}

	[Test]
	public async Task CodeFix_ObjectCreationWithVar_UsesExplicitTypeAndTargetTypedNew(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source = "var value = new Widget();";
		var document = CreateDocument(source);
		var diagnostic = (await GetDiagnosticsAsync(document, cancellationToken)).Single();
		var provider = new TargetTypedObjectCreationCodeFixProvider();
		var actions = new List<CodeAction>();
		var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);

		// Act
		await provider.RegisterCodeFixesAsync(context);
		var operations = await actions.Single().GetOperationsAsync(cancellationToken);
		var changedDocument = ((ApplyChangesOperation)operations.Single()).ChangedSolution.GetDocument(document.Id)!;
		var fixedSource = (await changedDocument.GetTextAsync(cancellationToken)).ToString();

		// Assert
		await Assert.That(fixedSource).IsEqualTo("Widget value = new();");
	}

	[Test]
	public async Task CodeFix_ObjectCreationWithArguments_UsesExplicitTypeAndTargetTypedNew(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source = """
			using System.Text;

			var sb = new StringBuilder("hello");
			""";
		const string expected = """
			using System.Text;

			StringBuilder sb = new("hello");
			""";
		var document = CreateDocument(source);
		var diagnostic = (await GetDiagnosticsAsync(document, cancellationToken)).Single();
		var provider = new TargetTypedObjectCreationCodeFixProvider();
		var actions = new List<CodeAction>();
		var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);

		// Act
		await provider.RegisterCodeFixesAsync(context);
		var operations = await actions.Single().GetOperationsAsync(cancellationToken);
		var changedDocument = ((ApplyChangesOperation)operations.Single()).ChangedSolution.GetDocument(document.Id)!;
		var fixedSource = (await changedDocument.GetTextAsync(cancellationToken)).ToString();

		// Assert
		await Assert.That(fixedSource).IsEqualTo(expected);
	}

	[Test]
	public async Task CodeFix_ObjectCreationWithInitializer_UsesExplicitTypeAndTargetTypedNew(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source = """
			class Widget
			{
				public int Id { get; set; }
			}

			var widget = new Widget { Id = 1 };
			""";
		const string expected = """
			class Widget
			{
				public int Id { get; set; }
			}

			Widget widget = new() { Id = 1 };
			""";
		var document = CreateDocument(source);
		var diagnostic = (await GetDiagnosticsAsync(document, cancellationToken)).Single();
		var provider = new TargetTypedObjectCreationCodeFixProvider();
		var actions = new List<CodeAction>();
		var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);

		// Act
		await provider.RegisterCodeFixesAsync(context);
		var operations = await actions.Single().GetOperationsAsync(cancellationToken);
		var changedDocument = ((ApplyChangesOperation)operations.Single()).ChangedSolution.GetDocument(document.Id)!;
		var fixedSource = (await changedDocument.GetTextAsync(cancellationToken)).ToString();

		// Assert
		await Assert.That(fixedSource).IsEqualTo(expected);
	}

	[Test]
	public async Task CodeFix_UsingVarWithObjectCreation_UsesExplicitTypeAndTargetTypedNew(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		const string source = """
			using System.IO;

			using var reader = new StreamReader(path);
			""";
		const string expected = """
			using System.IO;

			using StreamReader reader = new(path);
			""";
		var document = CreateDocument(source);
		var diagnostic = (await GetDiagnosticsAsync(document, cancellationToken)).Single();
		var provider = new TargetTypedObjectCreationCodeFixProvider();
		var actions = new List<CodeAction>();
		var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);

		// Act
		await provider.RegisterCodeFixesAsync(context);
		var operations = await actions.Single().GetOperationsAsync(cancellationToken);
		var changedDocument = ((ApplyChangesOperation)operations.Single()).ChangedSolution.GetDocument(document.Id)!;
		var fixedSource = (await changedDocument.GetTextAsync(cancellationToken)).ToString();

		// Assert
		await Assert.That(fixedSource).IsEqualTo(expected);
	}

	static Document CreateDocument(string source)
	{
		using var workspace = new AdhocWorkspace();
		var projectId = ProjectId.CreateNewId();
		var documentId = DocumentId.CreateNewId(projectId);
		var project = ProjectInfo
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

		return workspace
			.CurrentSolution.AddProject(project)
			.AddDocument(documentId, "Test.cs", SourceText.From(source), filePath: "Test.cs")
			.GetDocument(documentId)!;
	}

	static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
		Document document,
		CancellationToken cancellationToken
	)
	{
		var compilation = (await document.Project.GetCompilationAsync(cancellationToken))!;
		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new TargetTypedObjectCreationAnalyzer());
		return await compilation
			.WithAnalyzers(analyzers, document.Project.AnalyzerOptions)
			.GetAnalyzerDiagnosticsAsync(cancellationToken);
	}
}
