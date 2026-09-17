using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers.TargetTypedObjectCreation;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TargetTypedObjectCreationAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "PDS0003";

	static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		"Use an explicit type with target-typed object creation",
		"Use explicit type instead of 'var' with target-typed object creation",
		"Style",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Prefer an explicit type combined with a target-typed 'new()' expression over 'var'."
	);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

	public override void Initialize(AnalysisContext context)
	{
		if (context is null)
			throw new ArgumentNullException(nameof(context));

		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
	}

	static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
	{
		var declaration = ((LocalDeclarationStatementSyntax)context.Node).Declaration;
		if (!declaration.Type.IsVar || declaration.Variables.Count != 1)
			return;

		if (declaration.Variables[0].Initializer?.Value is ObjectCreationExpressionSyntax)
		{
			context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Type.GetLocation()));
		}
	}
}
