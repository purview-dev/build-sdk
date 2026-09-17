using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers.ExtensionsNamespace;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExtensionsNamespaceAnalyzer : DiagnosticAnalyzer
{
	internal const string DiagnosticId = "PDS0002";

	static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		"Extensions root folder resets namespace",
		"Namespace '{0}' does not match expected Extensions namespace '{1}'",
		"Naming",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Files under the project-root 'Extensions' folder derive their namespace from the folder structure, ignoring RootNamespace."
	);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

	public override void Initialize(AnalysisContext context)
	{
		if (context is null)
			throw new ArgumentNullException(nameof(context));

		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterSyntaxNodeAction(
			AnalyzeNamespaceDeclaration,
			SyntaxKind.NamespaceDeclaration,
			SyntaxKind.FileScopedNamespaceDeclaration
		);
	}

	static void AnalyzeNamespaceDeclaration(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not BaseNamespaceDeclarationSyntax namespaceDeclaration)
		{
			return;
		}

		var filePath = context.Node.SyntaxTree.FilePath;
		if (string.IsNullOrWhiteSpace(filePath))
			return;

		var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Node.SyntaxTree);
		if (!options.TryGetBuildProperty(BuildPropertyKeys.ProjectDir, out var projectDir))
			return;

		var expectedNamespace = ExtensionsNamespaceHelper.ComputeExpectedNamespace(projectDir, filePath);
		if (expectedNamespace is null)
		{
			return;
		}

		var actualNamespace = namespaceDeclaration.Name.ToString();
		if (string.Equals(actualNamespace, expectedNamespace, StringComparison.Ordinal))
		{
			return;
		}

		var diagnostic = Diagnostic.Create(
			Rule,
			namespaceDeclaration.Name.GetLocation(),
			actualNamespace,
			expectedNamespace
		);

		context.ReportDiagnostic(diagnostic);
	}
}
