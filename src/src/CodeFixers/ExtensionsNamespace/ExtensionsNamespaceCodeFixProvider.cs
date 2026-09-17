using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Purview.BuildSdk.Analyzers;
using Purview.BuildSdk.Analyzers.ExtensionsNamespace;

namespace Purview.BuildSdk.CodeFixers.ExtensionsNamespace;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExtensionsNamespaceCodeFixProvider))]
[Shared]
public sealed class ExtensionsNamespaceCodeFixProvider : CodeFixProvider
{
	internal const string SyncNamespaceToExtensionsFolderEquivalenceKey = "SyncNamespaceToExtensionsFolder";

	public override ImmutableArray<string> FixableDiagnosticIds => [ExtensionsNamespaceAnalyzer.DiagnosticId];

	public override FixAllProvider GetFixAllProvider()
	{
		return WellKnownFixAllProviders.BatchFixer;
	}

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
		if (root is null)
		{
			return;
		}

		foreach (var diagnostic in context.Diagnostics)
		{
			var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
			var namespaceDeclaration = node.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();
			if (namespaceDeclaration is null)
			{
				continue;
			}

			context.RegisterCodeFix(
				CodeAction.Create(
					"Sync namespace to Extensions folder structure",
					ct => ApplyFixAsync(context.Document, namespaceDeclaration, ct),
					equivalenceKey: SyncNamespaceToExtensionsFolderEquivalenceKey
				),
				diagnostic
			);
		}
	}

	static async Task<Document> ApplyFixAsync(
		Document document,
		BaseNamespaceDeclarationSyntax namespaceDeclaration,
		CancellationToken cancellationToken
	)
	{
		var root = await document.GetSyntaxRootAsync(cancellationToken);
		if (root is null)
		{
			return document;
		}

		if (!TryGetProjectDir(document, root.SyntaxTree, out var projectDir))
		{
			return document;
		}

		var expectedNamespace = ExtensionsNamespaceHelper.ComputeExpectedNamespace(
			projectDir,
			root.SyntaxTree.FilePath
		);
		if (expectedNamespace is null)
		{
			return document;
		}

		if (expectedNamespace.Length == 0)
		{
			var globalNamespaceDocument = RemoveNamespaceDeclaration(root, namespaceDeclaration);
			return document.WithSyntaxRoot(globalNamespaceDocument);
		}

		var expectedNamespaceName = SyntaxFactory
			.ParseName(expectedNamespace)
			.WithTriviaFrom(namespaceDeclaration.Name);

		var rewrittenNamespace = namespaceDeclaration switch
		{
			FileScopedNamespaceDeclarationSyntax fileScoped => fileScoped.WithName(expectedNamespaceName),
			NamespaceDeclarationSyntax blockScoped => blockScoped.WithName(expectedNamespaceName),
			_ => namespaceDeclaration,
		};

		var newRoot = root.ReplaceNode(namespaceDeclaration, rewrittenNamespace);
		return document.WithSyntaxRoot(newRoot);
	}

	static bool TryGetProjectDir(Document document, SyntaxTree syntaxTree, out string projectDir)
	{
		var options = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
		return options.TryGetBuildProperty(BuildPropertyKeys.ProjectDir, out projectDir);
	}

	static SyntaxNode RemoveNamespaceDeclaration(SyntaxNode root, BaseNamespaceDeclarationSyntax namespaceDeclaration)
	{
		if (namespaceDeclaration.Parent is CompilationUnitSyntax compilationUnit)
		{
			var index = compilationUnit.Members.IndexOf(namespaceDeclaration);
			if (index >= 0)
			{
				var members = compilationUnit.Members.RemoveAt(index).InsertRange(index, namespaceDeclaration.Members);
				return compilationUnit.WithMembers(members);
			}
		}

		if (namespaceDeclaration.Parent is NamespaceDeclarationSyntax parentNamespace)
		{
			var index = parentNamespace.Members.IndexOf(namespaceDeclaration);
			if (index >= 0)
			{
				var members = parentNamespace.Members.RemoveAt(index).InsertRange(index, namespaceDeclaration.Members);
				var rewrittenParent = parentNamespace.WithMembers(members);
				return root.ReplaceNode(parentNamespace, rewrittenParent);
			}
		}

		return root;
	}
}
