using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers.EditorBrowsable;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EditorBrowsableSuppressor : DiagnosticSuppressor
{
	static readonly SuppressionDescriptor SuppressMissingXmlDocs = new(
		"PDS0001",
		"CS1591",
		"EditorBrowsable(Never) members do not require XML docs."
	);

	public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => [SuppressMissingXmlDocs];

	public override void ReportSuppressions(SuppressionAnalysisContext context)
	{
		var editorBrowsableType = context.Compilation.GetTypeByMetadataName(typeof(EditorBrowsableAttribute).FullName!);
		if (editorBrowsableType is null)
		{
			return;
		}

		foreach (var diagnostic in context.ReportedDiagnostics)
		{
			var location = diagnostic.Location;
			if (!location.IsInSource || location.SourceTree is null)
			{
				continue;
			}

			var semanticModel = context.GetSemanticModel(location.SourceTree);
			var rootNode = location.SourceTree.GetRoot(context.CancellationToken);
			var node = rootNode.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
			var symbol = semanticModel.GetDeclaredSymbol(node, context.CancellationToken);

			if (symbol is not null && HasEditorBrowsableNever(symbol, editorBrowsableType))
			{
				context.ReportSuppression(Suppression.Create(SuppressMissingXmlDocs, diagnostic));
			}
		}
	}

	static bool HasEditorBrowsableNever(ISymbol symbol, INamedTypeSymbol editorBrowsableType)
	{
		foreach (var attribute in symbol.GetAttributes())
		{
			if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, editorBrowsableType))
			{
				continue;
			}

			if (
				attribute.ConstructorArguments.Length == 1
				&& attribute.ConstructorArguments[0].Value is int editorBrowsableState
				&& editorBrowsableState == (int)EditorBrowsableState.Never
			)
			{
				return true;
			}
		}

		return false;
	}
}
