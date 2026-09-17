using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers.EnglishNaming;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EnglishNamingAnalyzer : DiagnosticAnalyzer
{
	internal const string DiagnosticId = "PDS0004";

	static readonly DiagnosticDescriptor Rule = new(
		DiagnosticId,
		"Use correct acronym capitalization",
		"Identifier '{0}' uses incorrect acronym capitalization; prefer '{1}'",
		"Naming",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Acronyms such as 'Api' should be capitalized as 'API' for correct English. "
			+ "Well-known framework spellings (Sql, Guid, Uuid, Url, Dns, Http, Xml, ...) are exempt by default. "
			+ "Customize via dotnet_analyzer_configuration.pds0004.allowed_words, .acronym_map, and .allowed_identifiers."
	);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

	public override void Initialize(AnalysisContext context)
	{
		if (context is null)
		{
			throw new ArgumentNullException(nameof(context));
		}

		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			// Config options can differ per syntax tree (.editorconfig), so resolve and cache them
			// once per tree instead of parsing the lists for every symbol.
			var configCache = new ConcurrentDictionary<SyntaxTree, EnglishNamingConfig>();

			startContext.RegisterSymbolAction(
				symbolContext => AnalyzeSymbol(symbolContext, configCache),
				SymbolKind.NamedType,
				SymbolKind.Namespace,
				SymbolKind.Method,
				SymbolKind.Property,
				SymbolKind.Event,
				SymbolKind.Field
			);
		});
	}

	static void AnalyzeSymbol(
		SymbolAnalysisContext context,
		ConcurrentDictionary<SyntaxTree, EnglishNamingConfig> configCache
	)
	{
		var symbol = context.Symbol;
		if (symbol.IsImplicitlyDeclared)
		{
			return;
		}

		var declaringTree = symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree;
		if (declaringTree is null)
		{
			return;
		}

		var config = configCache.GetOrAdd(
			declaringTree,
			tree => EnglishNamingConfig.FromOptions(context.Options.AnalyzerConfigOptionsProvider.GetOptions(tree))
		);

		var corrected = EnglishNamingHelper.CorrectIdentifier(symbol.Name, config);
		if (corrected is null || string.Equals(symbol.Name, corrected, StringComparison.Ordinal))
		{
			return;
		}

		// Names mandated by an interface or base class cannot be changed without breaking the contract.
		if (EnglishNamingHelper.IsContractMember(symbol))
		{
			return;
		}

		foreach (var location in symbol.Locations)
		{
			if (location.IsInSource)
			{
				context.ReportDiagnostic(Diagnostic.Create(Rule, location, symbol.Name, corrected));
			}
		}
	}
}
