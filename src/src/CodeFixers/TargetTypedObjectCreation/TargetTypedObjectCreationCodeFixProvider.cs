using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Purview.BuildSdk.Analyzers.TargetTypedObjectCreation;

namespace Purview.BuildSdk.CodeFixers.TargetTypedObjectCreation;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TargetTypedObjectCreationCodeFixProvider))]
[Shared]
public sealed class TargetTypedObjectCreationCodeFixProvider : CodeFixProvider
{
	internal const string UseExplicitTypeAndTargetTypedNewEquivalenceKey = "UseExplicitTypeAndTargetTypedNew";

	public override ImmutableArray<string> FixableDiagnosticIds => [TargetTypedObjectCreationAnalyzer.DiagnosticId];

	public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
		if (root is null)
			return;

		foreach (var diagnostic in context.Diagnostics)
		{
			var node = root.FindNode(diagnostic.Location.SourceSpan);
			var declaration = node.FirstAncestorOrSelf<VariableDeclarationSyntax>();
			if (declaration is null || !declaration.Type.IsVar)
				continue;

			if (
				declaration.Variables.Count != 1
				|| declaration.Variables[0].Initializer?.Value is not ObjectCreationExpressionSyntax
			)
				continue;

			context.RegisterCodeFix(
				CodeAction.Create(
					"Use explicit type and target-typed new",
					cancellationToken => ApplyFixAsync(context.Document, declaration, cancellationToken),
					equivalenceKey: UseExplicitTypeAndTargetTypedNewEquivalenceKey
				),
				diagnostic
			);
		}
	}

	static async Task<Document> ApplyFixAsync(
		Document document,
		VariableDeclarationSyntax declaration,
		CancellationToken cancellationToken
	)
	{
		if (declaration.Variables[0].Initializer?.Value is not ObjectCreationExpressionSyntax objectCreation)
			return document;

		var explicitType = objectCreation.Type.WithTriviaFrom(declaration.Type);
		var targetTypedCreation = SyntaxFactory
			.ImplicitObjectCreationExpression(
				objectCreation.ArgumentList ?? SyntaxFactory.ArgumentList(),
				objectCreation.Initializer
			)
			.WithTriviaFrom(objectCreation);

		var rewrittenDeclaration = declaration.ReplaceNode(objectCreation, targetTypedCreation).WithType(explicitType);
		var root = await document.GetSyntaxRootAsync(cancellationToken);
		return root is null ? document : document.WithSyntaxRoot(root.ReplaceNode(declaration, rewrittenDeclaration));
	}
}
