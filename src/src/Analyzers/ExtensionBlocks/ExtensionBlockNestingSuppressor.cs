using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers.ExtensionBlocks;

/// <summary>
/// Suppresses <c>CA1034</c> (nested types should not be visible) on an <c>extension</c> block.
/// </summary>
/// <remarks>
/// <para>
/// A C# 14 extension block is compiled into a nested type inside its owning static class, so
/// <c>CA1034</c> fires on every one — reported at the <c>extension</c> keyword, naming nothing, because the
/// generated type has no source name ("Do not nest type ."). The rule's advice cannot be taken: the nesting
/// is the language's own encoding of the feature, not a design choice, and the only alternative would be a
/// <c>#pragma warning disable CA1034</c> or a <c>[SuppressMessage]</c> beside every extension block in every
/// consuming repository. <c>CA1034</c> ships at <c>error</c> in this SDK's <c>.editorconfig</c>, so without
/// this suppression an extension block does not merely warn, it fails the build.
/// </para>
/// <para>
/// The suppression is deliberately narrow: only the extension block's own declaration is suppressed. An
/// ordinary public type nested inside the same static class keeps the warning, because the innermost
/// declaration enclosing the reported location is the one that decides.
/// </para>
/// <para>
/// A repository that wants the warning back adds <c>PDS0007</c> to <c>NoWarn</c>, which restores
/// <c>CA1034</c> for every extension block.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExtensionBlockNestingSuppressor : DiagnosticSuppressor
{
	/// <summary>
	/// The syntax kind of an <c>extension</c> block declaration.
	/// </summary>
	/// <remarks>
	/// Compared as a raw kind value rather than by testing the node against
	/// <c>ExtensionBlockDeclarationSyntax</c>, so the suppressor still loads on a compiler host whose
	/// <c>Microsoft.CodeAnalysis.CSharp</c> predates that type. An enum member is a compile-time constant and
	/// carries no runtime dependency; a type test would be resolved when the method is jitted.
	/// </remarks>
	const int ExtensionBlockDeclarationKind = (int)SyntaxKind.ExtensionBlockDeclaration;

	/// <summary>
	/// The suppression of <c>CA1034</c> on an extension block.
	/// </summary>
	/// <remarks>
	/// This is not a rule and is never reported. It is the id a repository adds to <c>NoWarn</c> to turn the
	/// suppression off, and the id every suppression is logged under. Suppression descriptors are not
	/// release-tracked, so it must not appear in <c>AnalyzerReleases.Shipped.md</c>, which lists reported
	/// rules only.
	/// </remarks>
	public static readonly SuppressionDescriptor SuppressNestingOnExtensionBlocks = new(
		"PDS0007",
		"CA1034",
		"An extension block is compiled into a nested type inside its owning static class, so CA1034 reports a nesting the language mandates and the developer cannot remove."
	);

	/// <inheritdoc />
	public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => [SuppressNestingOnExtensionBlocks];

	/// <inheritdoc />
	public override void ReportSuppressions(SuppressionAnalysisContext context)
	{
		foreach (var diagnostic in context.ReportedDiagnostics)
		{
			// Only CA1034 is declared as suppressible, so this is the same bucket the descriptor names.
			if (
				!string.Equals(
					diagnostic.Id,
					SuppressNestingOnExtensionBlocks.SuppressedDiagnosticId,
					StringComparison.Ordinal
				)
			)
			{
				continue;
			}

			if (!IsExtensionBlockDeclaration(diagnostic, context.CancellationToken))
			{
				continue;
			}

			context.ReportSuppression(Suppression.Create(SuppressNestingOnExtensionBlocks, diagnostic));
		}
	}

	/// <summary>
	/// Determines whether the reported diagnostic sits on an extension block declaration.
	/// </summary>
	/// <remarks>
	/// <c>CA1034</c> is reported at the <c>extension</c> keyword, so the location is resolved to the nearest
	/// enclosing declaration that declares a type. The innermost one wins: an ordinary nested type inside a
	/// static class that also holds extension blocks keeps its warning.
	/// </remarks>
	static bool IsExtensionBlockDeclaration(Diagnostic diagnostic, CancellationToken cancellationToken)
	{
		var location = diagnostic.Location;
		if (!location.IsInSource || location.SourceTree is not { } tree)
		{
			return false;
		}

		var root = tree.GetRoot(cancellationToken);
		var node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);

		// Decided from syntax alone, with no semantic model: an extension block is a declaration form, so the
		// syntax tree already answers the question, and the generated type carries no name to match against.
		for (var current = node; current is not null; current = current.Parent)
		{
			if (!IsTypeDeclaration(current))
			{
				continue;
			}

			return current.RawKind == ExtensionBlockDeclarationKind;
		}

		return false;
	}

	static bool IsTypeDeclaration(SyntaxNode node) =>
		node.RawKind
			is (int)SyntaxKind.ClassDeclaration
				or (int)SyntaxKind.StructDeclaration
				or (int)SyntaxKind.InterfaceDeclaration
				or (int)SyntaxKind.RecordDeclaration
				or (int)SyntaxKind.RecordStructDeclaration
				or (int)SyntaxKind.EnumDeclaration
				or (int)SyntaxKind.DelegateDeclaration
				or ExtensionBlockDeclarationKind;
}
