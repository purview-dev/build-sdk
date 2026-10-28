using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Purview.BuildSdk.Analyzers.Bff;

/// <summary>
/// Reports an authentication cookie that a BFF (<c>IsBffProject=true</c>) configures in a way that undoes
/// what a BFF exists to provide: a session the browser holds but script cannot read and other sites cannot
/// send.
/// </summary>
/// <remarks>
/// Only an explicit assignment of a constant is reported, and only on the cookie of
/// <c>CookieAuthenticationOptions</c> - never on a <c>CookieBuilder</c> in general. The antiforgery cookie is
/// legitimately readable by script, and the OpenID Connect correlation and nonce cookies legitimately need
/// <c>SameSite=None</c>; reporting those would make the rule noise, and a noisy rule gets suppressed
/// wholesale. A cookie configured somewhere the compilation cannot see (another assembly, a local variable
/// holding the builder) is not reported: the rule trades those misses for never being wrong.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BffAuthenticationCookieAnalyzer : DiagnosticAnalyzer
{
	public const string HttpOnlyDiagnosticId = "PDS0008";
	public const string SameSiteNoneDiagnosticId = "PDS0009";

	const string CookieBuilderMetadataName = "Microsoft.AspNetCore.Http.CookieBuilder";
	const string CookieAuthenticationOptionsMetadataName =
		"Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions";
	const string SameSiteModeMetadataName = "Microsoft.AspNetCore.Http.SameSiteMode";

	// SameSiteMode.None. Unspecified is -1, Lax 1 and Strict 2.
	const int SameSiteModeNone = 0;

	static readonly DiagnosticDescriptor HttpOnlyRule = new(
		HttpOnlyDiagnosticId,
		"BFF authentication cookie must be HttpOnly",
		"The authentication cookie is configured with HttpOnly = false, so script running in the page can read the session",
		"Security",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "A BFF keeps the tokens on the server and hands the browser only a session cookie. Without HttpOnly, any script that runs in the page - including injected script - can read that cookie and replay the session from anywhere."
	);

	static readonly DiagnosticDescriptor SameSiteNoneRule = new(
		SameSiteNoneDiagnosticId,
		"BFF authentication cookie must not use SameSite=None",
		"The authentication cookie is configured with SameSite = None, so requests from other sites carry the session",
		"Security",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "SameSite=None makes the browser attach the BFF's session cookie to cross-site requests, which turns every BFF endpoint into a cross-site request forgery target. Use Strict, or Lax when top-level navigation from another site must arrive signed in."
	);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [HttpOnlyRule, SameSiteNoneRule];

	public override void Initialize(AnalysisContext context)
	{
		if (context is null)
			throw new ArgumentNullException(nameof(context));

		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(startContext =>
		{
			var globalOptions = startContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
			if (
				!globalOptions.TryGetBuildProperty(BuildPropertyKeys.IsBffProject, out var isBffProject)
				|| !string.Equals(isBffProject, "true", StringComparison.OrdinalIgnoreCase)
			)
			{
				return;
			}

			var compilation = startContext.Compilation;
			var cookieBuilder = compilation.GetTypeByMetadataName(CookieBuilderMetadataName);
			var cookieAuthenticationOptions = compilation.GetTypeByMetadataName(
				CookieAuthenticationOptionsMetadataName
			);
			var sameSiteMode = compilation.GetTypeByMetadataName(SameSiteModeMetadataName);

			// A BFF that does not reference cookie authentication has no authentication cookie to check.
			if (cookieBuilder is null || cookieAuthenticationOptions is null)
				return;

			startContext.RegisterOperationAction(
				operationContext =>
					AnalyzeAssignment(operationContext, cookieBuilder, cookieAuthenticationOptions, sameSiteMode),
				OperationKind.SimpleAssignment
			);
		});
	}

	static void AnalyzeAssignment(
		OperationAnalysisContext context,
		INamedTypeSymbol cookieBuilder,
		INamedTypeSymbol cookieAuthenticationOptions,
		INamedTypeSymbol? sameSiteMode
	)
	{
		var assignment = (ISimpleAssignmentOperation)context.Operation;
		if (assignment.Target is not IPropertyReferenceOperation target)
			return;

		if (!InheritsFrom(target.Property.ContainingType, cookieBuilder))
			return;

		if (!IsAuthenticationCookie(ResolveBuilder(assignment, target), cookieAuthenticationOptions))
			return;

		var value = assignment.Value.ConstantValue;
		if (!value.HasValue)
			return;

		if (target.Property.Name == "HttpOnly" && value.Value is false)
		{
			context.ReportDiagnostic(Diagnostic.Create(HttpOnlyRule, assignment.Syntax.GetLocation()));
		}
		else if (
			target.Property.Name == "SameSite"
			&& sameSiteMode is not null
			&& SymbolEqualityComparer.Default.Equals(target.Property.Type, sameSiteMode)
			&& value.Value is int mode
			&& mode == SameSiteModeNone
		)
		{
			context.ReportDiagnostic(Diagnostic.Create(SameSiteNoneRule, assignment.Syntax.GetLocation()));
		}
	}

	/// <summary>
	/// The operation that yields the builder being assigned to. For <c>options.Cookie.HttpOnly = false</c>
	/// that is the receiver itself; inside a nested initializer (<c>Cookie = { HttpOnly = false }</c>) the
	/// receiver is implicit, and the builder is the member the enclosing initializer initializes.
	/// </summary>
	static IOperation? ResolveBuilder(ISimpleAssignmentOperation assignment, IPropertyReferenceOperation target) =>
		target.Instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver }
		&& assignment.Parent is IObjectOrCollectionInitializerOperation { Parent: IMemberInitializerOperation member }
			? member.InitializedMember
			: target.Instance;

	/// <summary>
	/// True only for <c>options.Cookie</c> where <c>options</c> is <c>CookieAuthenticationOptions</c> (or a
	/// type derived from it).
	/// </summary>
	static bool IsAuthenticationCookie(IOperation? instance, INamedTypeSymbol cookieAuthenticationOptions) =>
		instance is IPropertyReferenceOperation { Property.Name: "Cookie" } cookieProperty
		&& InheritsFrom(cookieProperty.Property.ContainingType, cookieAuthenticationOptions);

	static bool InheritsFrom(INamedTypeSymbol? type, INamedTypeSymbol baseType)
	{
		for (var current = type; current is not null; current = current.BaseType)
		{
			if (SymbolEqualityComparer.Default.Equals(current, baseType))
				return true;
		}

		return false;
	}
}
