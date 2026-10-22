using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

// ITypeSymbol.IsUnion is the language's own answer to "is this a union", and is marked [Experimental]
// until C# unions ship. Targeting it deliberately is the whole point of this suppressor, so the
// experimental warning is disabled here rather than repo-wide.
#pragma warning disable RSEXPERIMENTAL006

namespace Purview.BuildSdk.Analyzers.Unions;

/// <summary>
/// Suppresses <c>CA1815</c> (override <c>Equals</c> and the equality operators on value types) on union
/// declarations.
/// </summary>
/// <remarks>
/// <para>
/// A union is a value type, so <c>CA1815</c> fires on every one. The members it asks for cannot sensibly be
/// written: C# does not synthesise value equality for a union declaration, and a union is read by matching
/// its active case rather than by comparing two unions. Analyser support for unions is planned for .NET 12,
/// so until then every union declaration in every consuming repository would otherwise need a
/// <c>#pragma warning disable CA1815</c> or a <c>[SuppressMessage]</c> beside it.
/// </para>
/// <para>
/// The suppression is deliberately narrow: it applies only where the reported declaration is itself a union.
/// A union's **case types** are ordinary value types and keep the warning, as does every other struct — if
/// the reported type is not a union, nothing is suppressed.
/// </para>
/// <para>
/// Both spellings count, because both produce a union and both are reported by <c>CA1815</c>:
/// </para>
/// <list type="bullet">
/// <item>the <c>union</c> keyword, which <see cref="ITypeSymbol.IsUnion"/> answers for; and</item>
/// <item><c>System.Runtime.CompilerServices.UnionAttribute</c>, which is also how the keyword form is encoded
/// in metadata, so a union read from a referenced assembly is recognised too.</item>
/// </list>
/// <para>
/// A repository that wants union equality declared turns the suppression off by adding <c>PDS0006</c> to
/// <c>NoWarn</c>, which brings <c>CA1815</c> back for every union.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnionEqualitySuppressor : DiagnosticSuppressor
{
	/// <summary>
	/// The metadata name of the attribute the compiler applies to a union.
	/// </summary>
	/// <remarks>
	/// Matched by name rather than by symbol identity. The attribute is embedded per-assembly when the
	/// targeted framework does not supply it, so a union from a referenced assembly carries that assembly's
	/// copy, and <see cref="SymbolEqualityComparer"/> would not match it against this compilation's.
	/// </remarks>
	const string UnionAttributeMetadataName = "System.Runtime.CompilerServices.UnionAttribute";

	/// <summary>
	/// The suppression of <c>CA1815</c> on a union declaration.
	/// </summary>
	/// <remarks>
	/// This is not a rule and is never reported. It is the id a repository adds to <c>NoWarn</c> to turn the
	/// suppression off, and the id every suppression is logged under. Suppression descriptors are not
	/// release-tracked, so it must not appear in <c>AnalyzerReleases.Shipped.md</c>, which lists reported
	/// rules only.
	/// </remarks>
	public static readonly SuppressionDescriptor SuppressEqualityOnUnions = new(
		"PDS0006",
		"CA1815",
		"A union is matched by its active case rather than compared by value, and C# does not synthesise value equality for a union declaration, so CA1815 asks for members a union cannot meaningfully provide. Analyser support for unions is planned for .NET 12."
	);

	/// <inheritdoc />
	public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => [SuppressEqualityOnUnions];

	/// <inheritdoc />
	public override void ReportSuppressions(SuppressionAnalysisContext context)
	{
		foreach (var diagnostic in context.ReportedDiagnostics)
		{
			// Only CA1815 is declared as suppressible, so this is the same bucket the descriptor names.
			if (
				!string.Equals(diagnostic.Id, SuppressEqualityOnUnions.SuppressedDiagnosticId, StringComparison.Ordinal)
			)
			{
				continue;
			}

			if (!IsUnionDeclaration(context, diagnostic))
			{
				continue;
			}

			context.ReportSuppression(Suppression.Create(SuppressEqualityOnUnions, diagnostic));
		}
	}

	/// <summary>
	/// Determines whether the reported diagnostic sits on a union declaration.
	/// </summary>
	/// <remarks>
	/// <c>CA1815</c> is reported at the declared type's identifier, so the location is resolved to the nearest
	/// enclosing declaration that declares a type. The innermost one wins: a union that nests an ordinary
	/// struct does not suppress the nested struct's warning.
	/// </remarks>
	static bool IsUnionDeclaration(SuppressionAnalysisContext context, Diagnostic diagnostic)
	{
		var location = diagnostic.Location;
		if (!location.IsInSource || location.SourceTree is not { } tree)
		{
			return false;
		}

		var semanticModel = context.GetSemanticModel(tree);
		var root = tree.GetRoot(context.CancellationToken);
		var node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);

		// Walked with the general GetDeclaredSymbol rather than by matching a syntax type, so the union
		// declaration syntax is recognised whatever shape the preview language feature settles on.
		for (var current = node; current is not null; current = current.Parent)
		{
			if (semanticModel.GetDeclaredSymbol(current, context.CancellationToken) is not INamedTypeSymbol type)
			{
				continue;
			}

			return type.IsUnion || HasUnionAttribute(type);
		}

		return false;
	}

	static bool HasUnionAttribute(INamedTypeSymbol type)
	{
		foreach (var attribute in type.GetAttributes())
		{
			if (
				attribute.AttributeClass is { } attributeClass
				&& string.Equals(
					attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
					"global::" + UnionAttributeMetadataName,
					StringComparison.Ordinal
				)
			)
			{
				return true;
			}
		}

		return false;
	}
}
