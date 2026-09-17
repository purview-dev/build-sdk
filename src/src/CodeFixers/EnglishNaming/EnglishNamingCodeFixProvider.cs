using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Rename;
using Purview.BuildSdk.Analyzers.EnglishNaming;

namespace Purview.BuildSdk.CodeFixers.EnglishNaming;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EnglishNamingCodeFixProvider))]
[Shared]
public sealed class EnglishNamingCodeFixProvider : CodeFixProvider
{
	internal const string RenameToCorrectEnglishEquivalenceKey = "RenameToCorrectEnglish";

	public override ImmutableArray<string> FixableDiagnosticIds => [EnglishNamingAnalyzer.DiagnosticId];

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

		var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
		if (semanticModel is null)
		{
			return;
		}

		var tree = await context.Document.GetSyntaxTreeAsync(context.CancellationToken);
		if (tree is null)
		{
			return;
		}

		var config = EnglishNamingConfig.FromOptions(
			context.Document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(tree)
		);

		foreach (var diagnostic in context.Diagnostics)
		{
			var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
			var symbol = GetDeclaredSymbol(semanticModel, node, context.CancellationToken);
			if (symbol is null)
			{
				continue;
			}

			// Names mandated by an interface or base class cannot be renamed without breaking the contract.
			if (EnglishNamingHelper.IsContractMember(symbol))
			{
				continue;
			}

			var corrected = EnglishNamingHelper.CorrectIdentifier(symbol.Name, config);
			if (corrected is null || string.Equals(symbol.Name, corrected, StringComparison.Ordinal))
			{
				continue;
			}

			context.RegisterCodeFix(
				CodeAction.Create(
					$"Rename to '{corrected}'",
					cancellationToken => RenameAsync(context.Document, symbol, corrected, cancellationToken),
					equivalenceKey: RenameToCorrectEnglishEquivalenceKey
				),
				diagnostic
			);
		}
	}

	static ISymbol? GetDeclaredSymbol(SemanticModel semanticModel, SyntaxNode node, CancellationToken cancellationToken)
	{
		for (var current = node; current is not null; current = current.Parent)
		{
			var symbol = semanticModel.GetDeclaredSymbol(current, cancellationToken);
			if (symbol is not null)
			{
				return symbol;
			}
		}

		return null;
	}

	static async Task<Solution> RenameAsync(
		Document document,
		ISymbol symbol,
		string newName,
		CancellationToken cancellationToken
	)
	{
		return await Renamer.RenameSymbolAsync(
			document.Project.Solution,
			symbol,
			new SymbolRenameOptions(),
			newName,
			cancellationToken
		);
	}
}
