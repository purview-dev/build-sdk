using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers.Unions;

/// <summary>
/// Integration tests for <see cref="UnionEqualitySuppressor"/> behaviour.
/// </summary>
/// <remarks>
/// <para>
/// CA1815 is reported by a stand-in analyzer rather than by the real CodeQuality rule, matching
/// <c>ExtensionsNamespaceSuppressorTests</c>. A suppressor only ever sees a diagnostic id and a location, so
/// the rule's own implementation is not what is under test — which declarations the suppression lands on is.
/// The stand-in reports on every struct, which is what the real rule does and which is why a union is caught
/// by it at all.
/// </para>
/// <para>
/// Every source here declares <c>UnionAttribute</c> and <c>IUnion</c> itself. Unions are a preview language
/// feature whose required runtime types are not in the <c>net10.0</c> framework this test host references, and
/// without <c>IUnion</c> the union declaration fails to bind with <c>CS0518</c> and no symbol is produced.
/// </para>
/// </remarks>
[Category("Integration")]
sealed class UnionEqualitySuppressorTests
{
	/// <summary>
	/// The runtime types a union declaration binds against, declared in source because the targeted framework
	/// does not supply them yet. The compiler embeds its own copies in exactly the same way.
	/// </summary>
	const string UnionRuntimeDeclarations = """
		namespace System.Runtime.CompilerServices
		{
			[global::System.AttributeUsage(global::System.AttributeTargets.Struct | global::System.AttributeTargets.Class)]
			public sealed class UnionAttribute : global::System.Attribute { }

			public interface IUnion { object? Value { get; } }
		}
		""";

	static async Task<Diagnostic[]> AnalyzeAsync(string source, CancellationToken cancellationToken)
	{
		var compilation = AnalyzerTestInfrastructure.CreateCompilation(
			source,
			@"C:\FakeProject\Errors.cs",
			LanguageVersion.Preview
		);

		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
			new FakeCa1815Analyzer(),
			new UnionEqualitySuppressor()
		);

		// reportSuppressedDiagnostics keeps a suppressed diagnostic in the results, carrying IsSuppressed.
		// Without it a suppressed diagnostic is simply filtered out, and "suppressed" would be
		// indistinguishable from "the stand-in analyzer never reported it" - so a broken stand-in would make
		// every suppression test pass for the wrong reason.
		CompilationWithAnalyzersOptions analysisOptions = new(
			AnalyzerTestInfrastructure.CreateAnalyzerOptions(),
			onAnalyzerException: null,
			concurrentAnalysis: false,
			logAnalyzerExecutionTime: false,
			reportSuppressedDiagnostics: true
		);

		var diagnostics = await compilation
			.WithAnalyzers(analyzers, analysisOptions)
			.GetAllDiagnosticsAsync(cancellationToken);

		return [.. diagnostics.Where(diagnostic => diagnostic.Id == "CA1815")];
	}

	static async Task AssertSuppressedAsync(Diagnostic[] diagnostics, string typeName, bool expected)
	{
		var diagnostic = diagnostics.SingleOrDefault(d => TargetName(d) == typeName);

		await Assert.That(diagnostic).IsNotNull();
		await Assert.That(diagnostic!.IsSuppressed).IsEqualTo(expected);
	}

	const string KeywordUnionSource = $$"""
		{{UnionRuntimeDeclarations}}

		namespace Sample
		{
			public readonly record struct NotFound(int Id);

			public readonly record struct Disabled(int Id);

			public readonly union TenantError(NotFound, Disabled);
		}
		""";

	[Test]
	public async Task Suppressor_GivenAUnionDeclaredWithTheKeyword_SuppressesCa1815(CancellationToken cancellationToken)
	{
		var diagnostics = await AnalyzeAsync(KeywordUnionSource, cancellationToken);

		await AssertSuppressedAsync(diagnostics, "TenantError", expected: true);
	}

	/// <summary>
	/// The case types of a union are ordinary value types and keep the warning. This is the assertion that
	/// stops the suppression widening into "every struct near a union".
	/// </summary>
	[Test]
	[Arguments("NotFound")]
	[Arguments("Disabled")]
	public async Task Suppressor_GivenAUnionsCaseType_DoesNotSuppressCa1815(
		string caseTypeName,
		CancellationToken cancellationToken
	)
	{
		var diagnostics = await AnalyzeAsync(KeywordUnionSource, cancellationToken);

		await AssertSuppressedAsync(diagnostics, caseTypeName, expected: false);
	}

	[Test]
	public async Task Suppressor_GivenAnOrdinaryStruct_DoesNotSuppressCa1815(CancellationToken cancellationToken)
	{
		const string source = $$"""
			{{UnionRuntimeDeclarations}}

			namespace Sample
			{
				public struct Money
				{
					public decimal Amount;
				}
			}
			""";

		var diagnostics = await AnalyzeAsync(source, cancellationToken);

		await AssertSuppressedAsync(diagnostics, "Money", expected: false);
	}

	[Test]
	public async Task Suppressor_GivenATypeCarryingUnionAttribute_SuppressesCa1815(CancellationToken cancellationToken)
	{
		const string source = $$"""
			{{UnionRuntimeDeclarations}}

			namespace Sample
			{
				[global::System.Runtime.CompilerServices.Union]
				public struct AttributedUnion
				{
					public int Tag;
				}
			}
			""";

		var diagnostics = await AnalyzeAsync(source, cancellationToken);

		await AssertSuppressedAsync(diagnostics, "AttributedUnion", expected: true);
	}

	/// <summary>
	/// The innermost declaration wins, so a union does not suppress the warning for a struct nested inside it.
	/// </summary>
	[Test]
	public async Task Suppressor_GivenAStructNestedInAUnion_SuppressesOnlyTheUnion(CancellationToken cancellationToken)
	{
		const string source = $$"""
			{{UnionRuntimeDeclarations}}

			namespace Sample
			{
				[global::System.Runtime.CompilerServices.Union]
				public struct OuterUnion
				{
					public int Tag;

					public struct Nested
					{
						public int Value;
					}
				}
			}
			""";

		var diagnostics = await AnalyzeAsync(source, cancellationToken);

		await AssertSuppressedAsync(diagnostics, "OuterUnion", expected: true);
		await AssertSuppressedAsync(diagnostics, "Nested", expected: false);
	}

	static string TargetName(Diagnostic diagnostic) => diagnostic.Properties["TypeName"]!;

	/// <summary>
	/// Reports CA1815 on every struct, the way the real rule does, carrying the type name so a test can tell
	/// the reports apart.
	/// </summary>
	[SuppressMessage(
		"MicrosoftCodeAnalysisDesign",
		"RS1029:Do not use reserved diagnostic IDs",
		Justification = "The id is deliberately the real CA1815, because that is the id the suppressor under test declares as suppressible. This analyzer is a test stand-in and never ships."
	)]
	[DiagnosticAnalyzer(LanguageNames.CSharp)]
	sealed class FakeCa1815Analyzer : DiagnosticAnalyzer
	{
		static readonly DiagnosticDescriptor Rule = new(
			"CA1815",
			"Override equals and operator equals on value types",
			"Override equals and operator equals on value types",
			"Performance",
			DiagnosticSeverity.Warning,
			isEnabledByDefault: true
		);

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

		public override void Initialize(AnalysisContext context)
		{
			context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
			context.EnableConcurrentExecution();
			context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
		}

		static void AnalyzeType(SymbolAnalysisContext context)
		{
			if (
				context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Struct } type
				|| type.Locations.FirstOrDefault(location => location.IsInSource) is not { } location
			)
			{
				return;
			}

			context.ReportDiagnostic(
				Diagnostic.Create(Rule, location, ImmutableDictionary<string, string?>.Empty.Add("TypeName", type.Name))
			);
		}
	}
}
