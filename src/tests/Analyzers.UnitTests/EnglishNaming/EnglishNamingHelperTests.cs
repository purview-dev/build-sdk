using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers.EnglishNaming;

/// <summary>
/// Unit tests for <see cref="EnglishNamingHelper"/> acronym-capitalization logic and
/// <see cref="EnglishNamingConfig"/> .editorconfig-driven customisation.
/// </summary>
[Category("Unit")]
public sealed class EnglishNamingHelperTests
{
	static readonly EnglishNamingConfig Defaults = EnglishNamingConfig.Create(
		EnglishNamingHelper.DefaultAcronymMap,
		EnglishNamingHelper.DefaultAllowedWords
	);

	[Test]
	[Arguments("Api", "API", DisplayName = "Single acronym")]
	[Arguments("Ai", "AI", DisplayName = "Two-letter acronym")]
	[Arguments("OpenApi", "OpenAPI", DisplayName = "Acronym after leading word")]
	[Arguments("ApiClient", "APIClient", DisplayName = "Acronym prefix")]
	[Arguments("IApiClient", "IAPIClient", DisplayName = "Interface I prefix preserved")]
	[Arguments("ApiController", "APIController", DisplayName = "Common MVC pattern")]
	[Arguments("Ui", "UI", DisplayName = "UI acronym")]
	[Arguments("Io", "IO", DisplayName = "IO acronym")]
	[Arguments("Os", "OS", DisplayName = "OS acronym")]
	[Arguments("CpuService", "CPUService", DisplayName = "CPU acronym")]
	[Arguments("GpuService", "GPUService", DisplayName = "GPU acronym")]
	[Arguments("Cli", "CLI", DisplayName = "CLI acronym")]
	[Arguments("Gui", "GUI", DisplayName = "GUI acronym")]
	[Arguments("RamSize", "RAMSize", DisplayName = "RAM acronym")]
	[Arguments("SshClient", "SSHClient", DisplayName = "SSH acronym")]
	[Arguments("GetApiToken", "GetAPIToken", DisplayName = "Acronym mid-method-name")]
	public async Task CorrectIdentifier_ReturnsCorrectedName(string name, string corrected)
	{
		var result = EnglishNamingHelper.CorrectIdentifier(name, Defaults);
		await Assert.That(result).IsEqualTo(corrected);
	}

	[Test]
	[Arguments("HttpClient", DisplayName = "Http is exempt")]
	[Arguments("XmlReader", DisplayName = "Xml is exempt")]
	[Arguments("JsonSerializer", DisplayName = "Json is exempt")]
	[Arguments("GetId", DisplayName = "Id is exempt")]
	[Arguments("SdkClient", DisplayName = "Sdk is exempt (too prevalent)")]
	[Arguments("BuildSdk", DisplayName = "Sdk embedded mid-name is exempt")]
	[Arguments("SqlConnection", DisplayName = "Sql is exempt (known .NET type)")]
	[Arguments("UrlBuilder", DisplayName = "Url is exempt (known .NET spelling)")]
	[Arguments("Uuid", DisplayName = "Uuid is exempt (known .NET spelling)")]
	[Arguments("Guid", DisplayName = "Guid is exempt (known .NET type)")]
	[Arguments("DnsResolver", DisplayName = "Dns is exempt (known .NET type)")]
	[Arguments("TcpClient", DisplayName = "Tcp is exempt (known .NET type)")]
	[Arguments("UdpClient", DisplayName = "Udp is exempt (known .NET type)")]
	[Arguments("SmtpClient", DisplayName = "Smtp is exempt (known .NET type)")]
	[Arguments("FtpClient", DisplayName = "Ftp is exempt")]
	[Arguments("ImapClient", DisplayName = "Imap is exempt")]
	[Arguments("HtmlDocument", DisplayName = "Html is exempt (known .NET spelling)")]
	[Arguments("CssSelector", DisplayName = "Css is exempt")]
	[Arguments("CsvParser", DisplayName = "Csv is exempt")]
	[Arguments("DbContext", DisplayName = "Db is exempt (prevalent EF Core spelling)")]
	[Arguments("DbConnection", DisplayName = "DbConnection is exempt")]
	[Arguments("DbSet", DisplayName = "DbSet is exempt")]
	[Arguments("CreateDbContext", DisplayName = "CreateDbContext is exempt")]
	[Arguments("OpenAPI", DisplayName = "Already correct acronym")]
	[Arguments("HTTPClient", DisplayName = "Already correct acronym run")]
	[Arguments("Client", DisplayName = "No acronyms present")]
	public async Task CorrectIdentifier_ReturnsNull_WhenAlreadyCorrect(string name)
	{
		var result = EnglishNamingHelper.CorrectIdentifier(name, Defaults);
		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task CorrectIdentifier_AllowedWordsWin_OverAcronymMap()
	{
		var config = EnglishNamingConfig.Create(
			ImmutableDictionary.CreateRange(StringComparer.Ordinal, new Dictionary<string, string> { ["Sdk"] = "SDK" }),
			EnglishNamingHelper.DefaultAllowedWords
		);

		// 'Sdk' is in the map but also in the default allowed words, so it stays unchanged.
		var result = EnglishNamingHelper.CorrectIdentifier("SdkClient", config);
		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task CorrectIdentifier_HonoursCustomAllowedWords()
	{
		var config = EnglishNamingConfig.Create(
			EnglishNamingHelper.DefaultAcronymMap,
			ImmutableHashSet.Create(StringComparer.Ordinal, "Api")
		);

		// 'Api' allowed via config, so it is not corrected even though the default map contains it.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsNull();
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("UiApi", config)).IsEqualTo("UIApi");
	}

	[Test]
	public async Task CorrectIdentifier_HonoursReplacementAcronymMap()
	{
		var config = EnglishNamingConfig.Create(
			ImmutableDictionary.CreateRange(StringComparer.Ordinal, new Dictionary<string, string> { ["Foo"] = "FOO" }),
			EnglishNamingHelper.DefaultAllowedWords
		);

		// The replacement map no longer contains 'Api', so only 'Foo' is corrected.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsNull();
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("FooClient", config)).IsEqualTo("FOOClient");
	}

	[Test]
	public async Task FromOptions_Defaults_AreUsed_WhenNoOverridesPresent()
	{
		var config = EnglishNamingConfig.FromOptions(new TestOptions([]));
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsEqualTo("API");
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("SdkClient", config)).IsNull();
	}

	[Test]
	public async Task FromOptions_AllowedWordsOption_MergesWithDefaults()
	{
		var options = new TestOptions(
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				[EnglishNamingHelper.AllowedWordsOptionKey] = "Http;Xml;Json;Id;Sdk;Api",
			}
		);

		var config = EnglishNamingConfig.FromOptions(options);
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsNull();
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("OpenApi", config)).IsNull();
		// Other acronyms are still corrected when the option only adds to the allowed words.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Ui", config)).IsEqualTo("UI");
	}

	[Test]
	public async Task FromOptions_AllowedWordsOption_CanOptOutOfDefaultRename()
	{
		var options = new TestOptions(
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				[EnglishNamingHelper.AllowedWordsOptionKey] = "Cli",
			}
		);

		var config = EnglishNamingConfig.FromOptions(options);
		// 'Cli' is a default rename, but the config exemption wins.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Cli", config)).IsNull();
		// Other default renames and exemptions are retained.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Cpu", config)).IsEqualTo("CPU");
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsEqualTo("API");
	}

	[Test]
	public async Task FromOptions_AcronymMapOption_MergesWithDefaults()
	{
		var options = new TestOptions(
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				[EnglishNamingHelper.AcronymMapOptionKey] = "Sql:SQL;Sdk:SDK",
			}
		);

		var config = EnglishNamingConfig.FromOptions(options);
		// Config map entries override the defaults: 'Sql' is default-exempt but now renamed.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("SqlConnection", config)).IsEqualTo("SQLConnection");
		// 'Sdk' is default-exempt but now renamed because the config mapped it.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("SdkClient", config)).IsEqualTo("SDKClient");
		// Defaults are retained for anything the config does not mention.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsEqualTo("API");
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("HttpClient", config)).IsNull();
	}

	[Test]
	public async Task FromOptions_AllowedIdentifiersOption_ExemptsBrandNames()
	{
		var options = new TestOptions(
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				[EnglishNamingHelper.AllowedIdentifiersOptionKey] = "CosmosDb",
			}
		);

		var config = EnglishNamingConfig.FromOptions(options);
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("CosmosDb", config)).IsNull();
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("CosmosDbContext", config)).IsNull();
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("CosmosDbServer", config)).IsNull();
		// Unrelated identifiers are still renamed by the word rules.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("ApiContext", config)).IsEqualTo("APIContext");
	}

	[Test]
	public async Task FromOptions_AcronymMapOption_ReenablesDbRename()
	{
		var options = new TestOptions(
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				[EnglishNamingHelper.AcronymMapOptionKey] = "Db:DB",
			}
		);

		var config = EnglishNamingConfig.FromOptions(options);
		// 'Db' is default-exempt, but a repo that prefers 'DB' re-enables the rename via config.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("DbContext", config)).IsEqualTo("DBContext");
		// Defaults are retained for anything the config does not mention.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api", config)).IsEqualTo("API");
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("HttpClient", config)).IsNull();
	}

	[Test]
	public async Task CorrectIdentifier_AllowedIdentifiers_FirstMatchWins_WithPrefixBoundary()
	{
		var config = EnglishNamingConfig.Create(
			EnglishNamingHelper.DefaultAcronymMap,
			EnglishNamingHelper.DefaultAllowedWords,
			["ICosmosDBService", "CosmosDb"]
		);

		// Exact match on the first, more specific entry.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("ICosmosDBService", config)).IsNull();
		// Word-boundary prefix match on the second entry.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("CosmosDbServer", config)).IsNull();
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("CosmosDbContext", config)).IsNull();
		// No match: normal word rules still apply.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("ApiContext", config)).IsEqualTo("APIContext");
	}

	[Test]
	public async Task CorrectIdentifier_AllowedIdentifiers_RequiresWordBoundary()
	{
		var config = EnglishNamingConfig.Create(
			EnglishNamingHelper.DefaultAcronymMap,
			EnglishNamingHelper.DefaultAllowedWords,
			["Api"]
		);

		// 'ApiClient' continues at a new word, so it is exempt.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("ApiClient", config)).IsNull();
		// 'Api2' continues mid-name with a non-boundary character, so it is not exempt (and 'Api2'
		// is a single un-flagged word, so it is left unchanged).
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("Api2", config)).IsNull();

		// 'CosmosDb' starts a new word after 'Cosmos', so it is exempt.
		var cosmosConfig = EnglishNamingConfig.Create(
			EnglishNamingHelper.DefaultAcronymMap,
			EnglishNamingHelper.DefaultAllowedWords,
			["Cosmos"]
		);
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("CosmosDb", cosmosConfig)).IsNull();
		// 'CosmosdbApi' has no word boundary after 'Cosmos' (next char is lowercase), so it is not
		// exempt and the trailing 'Api' segment is still renamed.
		await Assert.That(EnglishNamingHelper.CorrectIdentifier("CosmosdbApi", cosmosConfig)).IsEqualTo("CosmosdbAPI");
	}

	sealed class TestOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
	{
		public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
	}
}
