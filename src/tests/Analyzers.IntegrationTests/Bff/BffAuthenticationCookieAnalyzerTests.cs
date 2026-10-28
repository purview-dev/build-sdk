using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers.Bff;

/// <summary>
/// Verifies the BFF authentication-cookie rules (PDS0008, PDS0009) report only an explicit constant on the
/// <c>CookieAuthenticationOptions</c> cookie, and only in a project the SDK classifies as a BFF.
/// </summary>
/// <remarks>
/// The ASP.NET Core types are declared as stand-ins in the test source: the analyzer matches them by
/// metadata name, so a stand-in exercises exactly the lookup a real BFF compilation goes through.
/// </remarks>
sealed class BffAuthenticationCookieAnalyzerTests
{
	const string AspNetCoreStandIns = """
		namespace Microsoft.AspNetCore.Http
		{
			public enum SameSiteMode { Unspecified = -1, None = 0, Lax = 1, Strict = 2 }

			public class CookieBuilder
			{
				public virtual bool HttpOnly { get; set; }
				public virtual SameSiteMode SameSite { get; set; }
			}
		}

		namespace Microsoft.AspNetCore.Antiforgery
		{
			public class AntiforgeryOptions
			{
				public Microsoft.AspNetCore.Http.CookieBuilder Cookie { get; set; } = new();
			}
		}

		namespace Microsoft.AspNetCore.Authentication.OpenIdConnect
		{
			public class OpenIdConnectOptions
			{
				public Microsoft.AspNetCore.Http.CookieBuilder CorrelationCookie { get; set; } = new();
				public Microsoft.AspNetCore.Http.CookieBuilder NonceCookie { get; set; } = new();
			}
		}

		namespace Microsoft.AspNetCore.Authentication.Cookies
		{
			public class CookieAuthenticationOptions
			{
				public Microsoft.AspNetCore.Http.CookieBuilder Cookie { get; set; } = new();
			}
		}
		""";

	[Test]
	public async Task HttpOnlyFalse_OnAuthenticationCookie_InBff_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		var diagnostics = await GetDiagnosticsAsync(
			"""
			static class Setup
			{
				public static void Configure(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions options)
				{
					options.Cookie.HttpOnly = false;
				}
			}
			""",
			isBffProject: true,
			cancellationToken
		);

		await Assert
			.That(diagnostics.Select(d => d.Id))
			.IsEquivalentTo([BffAuthenticationCookieAnalyzer.HttpOnlyDiagnosticId]);
	}

	[Test]
	public async Task SameSiteNone_OnAuthenticationCookie_InBff_ReportsDiagnostic(CancellationToken cancellationToken)
	{
		var diagnostics = await GetDiagnosticsAsync(
			"""
			using Microsoft.AspNetCore.Http;

			static class Setup
			{
				public static void Configure(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions options)
				{
					options.Cookie.SameSite = SameSiteMode.None;
				}
			}
			""",
			isBffProject: true,
			cancellationToken
		);

		await Assert
			.That(diagnostics.Select(d => d.Id))
			.IsEquivalentTo([BffAuthenticationCookieAnalyzer.SameSiteNoneDiagnosticId]);
	}

	[Test]
	public async Task NestedCookieInitializer_InBff_ReportsBothDiagnostics(CancellationToken cancellationToken)
	{
		var diagnostics = await GetDiagnosticsAsync(
			"""
			using Microsoft.AspNetCore.Http;
			using Microsoft.AspNetCore.Authentication.Cookies;

			static class Setup
			{
				public static CookieAuthenticationOptions Create() =>
					new() { Cookie = { HttpOnly = false, SameSite = SameSiteMode.None } };
			}
			""",
			isBffProject: true,
			cancellationToken
		);

		await Assert
			.That(diagnostics.Select(d => d.Id))
			.IsEquivalentTo([
				BffAuthenticationCookieAnalyzer.HttpOnlyDiagnosticId,
				BffAuthenticationCookieAnalyzer.SameSiteNoneDiagnosticId,
			]);
	}

	[Test]
	public async Task SecureValues_OnAuthenticationCookie_InBff_DoNotReportDiagnostics(
		CancellationToken cancellationToken
	)
	{
		var diagnostics = await GetDiagnosticsAsync(
			"""
			using Microsoft.AspNetCore.Http;

			static class Setup
			{
				public static void Configure(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions options)
				{
					options.Cookie.HttpOnly = true;
					options.Cookie.SameSite = SameSiteMode.Strict;
					options.Cookie.SameSite = SameSiteMode.Lax;
				}
			}
			""",
			isBffProject: true,
			cancellationToken
		);

		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	public async Task OtherCookies_InBff_DoNotReportDiagnostics(CancellationToken cancellationToken)
	{
		// The antiforgery cookie is legitimately readable by script (an SPA echoes it in a header), and the
		// OpenID Connect correlation and nonce cookies need SameSite=None to survive the identity provider's
		// cross-site redirect. Reporting any of them would make the rule noise.
		var diagnostics = await GetDiagnosticsAsync(
			"""
			using Microsoft.AspNetCore.Http;

			static class Setup
			{
				public static void Configure(
					Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions antiforgery,
					Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions oidc,
					CookieBuilder standalone
				)
				{
					antiforgery.Cookie.HttpOnly = false;
					antiforgery.Cookie.SameSite = SameSiteMode.None;
					oidc.CorrelationCookie.SameSite = SameSiteMode.None;
					oidc.NonceCookie.SameSite = SameSiteMode.None;
					standalone.HttpOnly = false;
				}
			}
			""",
			isBffProject: true,
			cancellationToken
		);

		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	public async Task NonConstantValue_InBff_DoesNotReportDiagnostic(CancellationToken cancellationToken)
	{
		var diagnostics = await GetDiagnosticsAsync(
			"""
			static class Setup
			{
				public static void Configure(
					Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions options,
					bool httpOnly
				)
				{
					options.Cookie.HttpOnly = httpOnly;
				}
			}
			""",
			isBffProject: true,
			cancellationToken
		);

		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	[Arguments(false)]
	[Arguments(null)]
	public async Task HttpOnlyFalse_OutsideBff_DoesNotReportDiagnostic(
		bool? isBffProject,
		CancellationToken cancellationToken
	)
	{
		var diagnostics = await GetDiagnosticsAsync(
			"""
			static class Setup
			{
				public static void Configure(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions options)
				{
					options.Cookie.HttpOnly = false;
				}
			}
			""",
			isBffProject,
			cancellationToken
		);

		await Assert.That(diagnostics).IsEmpty();
	}

	static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
		string source,
		bool? isBffProject,
		CancellationToken cancellationToken
	)
	{
		var compilation = AnalyzerTestInfrastructure
			.CreateCompilation(source, @"C:\FakeProject\Setup.cs")
			.AddSyntaxTrees(
				Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
					AspNetCoreStandIns,
					path: AnalyzerTestInfrastructure.NormalizeFakePath(@"C:\FakeProject\AspNetCore.cs"),
					cancellationToken: cancellationToken
				)
			);

		var buildProperties = isBffProject is { } value
			? new Dictionary<string, string> { ["IsBffProject"] = value ? "true" : "false" }
			: null;

		var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new BffAuthenticationCookieAnalyzer());
		return await compilation
			.WithAnalyzers(
				analyzers,
				AnalyzerTestInfrastructure.CreateAnalyzerOptions(buildProperties: buildProperties)
			)
			.GetAnalyzerDiagnosticsAsync(cancellationToken);
	}
}
