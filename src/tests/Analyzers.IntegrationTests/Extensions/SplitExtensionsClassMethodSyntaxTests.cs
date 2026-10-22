using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Purview.BuildSdk.CodeFixers.ExtensionsNamespace;

namespace Purview.BuildSdk.Analyzers.IntegrationTests.Extensions;

/// <summary>
/// Covers <see cref="SplitExtensionsClassCodeRefactoringProvider.GetMethodSyntax"/>, which resolves a method
/// symbol back to its declaration.
/// </summary>
/// <remarks>
/// It used to throw when a symbol had no source declaration. A refactoring that throws takes down the
/// light-bulb for the whole action with an exception the developer can neither read nor act on, so the
/// unresolvable method is skipped instead. The null path is defensive — every method the refactoring groups
/// comes from the source document it was invoked on — which is exactly why it is worth pinning directly:
/// there is no input to the public entry point that reaches it.
/// </remarks>
[Category("Integration")]
sealed class SplitExtensionsClassMethodSyntaxTests
{
	const string Source = """
		using System.Collections.Generic;
		using System.Linq;

		namespace Sample
		{
			public static class ThingExtensions
			{
				public static string Describe(this string value) => value;
			}
		}
		""";

	static CSharpCompilation CreateCompilation() =>
		AnalyzerTestInfrastructure.CreateCompilation(Source, @"C:\FakeProject\ThingExtensions.cs");

	[Test]
	public async Task GetMethodSyntax_GivenASourceDeclaredMethod_ReturnsItsDeclaration()
	{
		var compilation = CreateCompilation();
		var extensions = compilation.GetTypeByMetadataName("Sample.ThingExtensions");

		await Assert.That(extensions).IsNotNull();

		var method = extensions!.GetMembers("Describe").OfType<IMethodSymbol>().Single();

		var syntax = SplitExtensionsClassCodeRefactoringProvider.GetMethodSyntax(method);

		await Assert.That(syntax).IsNotNull();
		await Assert.That(syntax!.Identifier.Text).IsEqualTo("Describe");
	}

	/// <summary>
	/// A method from metadata has no <see cref="ISymbol.DeclaringSyntaxReferences"/> at all, which is the
	/// shape that used to throw.
	/// </summary>
	[Test]
	public async Task GetMethodSyntax_GivenAMetadataMethod_ReturnsNull()
	{
		var compilation = CreateCompilation();
		var enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");

		await Assert.That(enumerable).IsNotNull();

		var method = enumerable!.GetMembers("Select").OfType<IMethodSymbol>().First();

		await Assert.That(method.DeclaringSyntaxReferences).IsEmpty();
		await Assert.That(SplitExtensionsClassCodeRefactoringProvider.GetMethodSyntax(method)).IsNull();
	}

	/// <summary>
	/// A symbol whose declaration is not a method declaration - a primary constructor's synthesised members,
	/// a record's generated equality members - must also come back as null rather than failing a cast.
	/// </summary>
	[Test]
	public async Task GetMethodSyntax_GivenASymbolWhoseDeclarationIsNotAMethod_ReturnsNull()
	{
		const string recordSource = """
			namespace Sample
			{
				public record Thing(int Id);
			}
			""";

		var compilation = AnalyzerTestInfrastructure.CreateCompilation(recordSource, @"C:\FakeProject\Thing.cs");

		var thing = compilation.GetTypeByMetadataName("Sample.Thing");

		await Assert.That(thing).IsNotNull();

		// The primary constructor's declaration is the record declaration, not a MethodDeclarationSyntax.
		var constructor = thing!
			.GetMembers(".ctor")
			.OfType<IMethodSymbol>()
			.First(method => method.Parameters.Length == 1);

		await Assert.That(SplitExtensionsClassCodeRefactoringProvider.GetMethodSyntax(constructor)).IsNull();
	}
}
