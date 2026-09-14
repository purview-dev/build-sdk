using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.DotNetProjectSdk.Analyzers.EnglishNaming;

/// <summary>
/// Correct-English acronym capitalization rules backing <see cref="EnglishNamingAnalyzer"/> (PDS0004).
/// The defaults deliberately follow .NET naming guidance for well-known framework type names
/// (<c>Sql</c>, <c>Guid</c>, <c>Uuid</c>, <c>Url</c>, <c>Dns</c>, <c>Tcp</c>, <c>Http</c>, <c>Xml</c>, ...),
/// while still enforcing uppercase for a small set of acronyms (<c>Api</c>, <c>Ai</c>, <c>Ui</c>,
/// <c>Io</c>, <c>Os</c>, <c>Cpu</c>, <c>Gpu</c>, <c>Cli</c>, <c>Gui</c>, <c>Ram</c>, <c>Ssh</c>).
/// Per-repo customisation is available through <c>.editorconfig</c>:
/// <list type="bullet">
/// <item><c>dotnet_analyzer_configuration.pds0004.allowed_words</c> — semicolon-separated segments that
/// must never be flagged (merged with the defaults; config entries override them).</item>
/// <item><c>dotnet_analyzer_configuration.pds0004.acronym_map</c> — semicolon-separated
/// <c>Key:Value</c> pairs overriding the default map (merged with the defaults; config entries win).</item>
/// <item><c>dotnet_analyzer_configuration.pds0004.allowed_identifiers</c> — semicolon-separated whole
/// identifiers that are never flagged, matched in order (first match wins) by exact name or
/// word-boundary prefix, so a brand name such as <c>CosmosDb</c> also covers
/// <c>CosmosDbServer</c>/<c>CosmosDbContext</c> while leaving <c>Db</c> to be renamed elsewhere.</item>
/// </list>
/// </summary>
static class EnglishNamingHelper
{
	/// <summary>
	/// Segments that are exempt from acronym capitalization even though they look like acronyms.
	/// These follow the official .NET guidance for well-known framework spellings (<c>Http</c>, <c>Xml</c>,
	/// <c>Json</c>, <c>Id</c>, <c>Sql</c>, <c>Url</c>, <c>Uuid</c>, <c>Dns</c>, <c>Tcp</c>, ...) plus
	/// <c>Sdk</c>, which is too prevalent across the ecosystem to flag, and <c>Db</c> — the segment behind
	/// the widely used <c>DbContext</c>/<c>DbConnection</c>/<c>DbSet</c> framework spellings.
	/// </summary>
	internal static readonly ImmutableHashSet<string> DefaultAllowedWords = ImmutableHashSet.Create(
		StringComparer.Ordinal,
		"Http",
		"Xml",
		"Json",
		"Id",
		"Sdk",
		"Sql",
		"Uuid",
		"Url",
		"Dns",
		"Tcp",
		"Udp",
		"Csv",
		"Pdf",
		"Html",
		"Css",
		"Ftp",
		"Smtp",
		"Imap",
		"Db"
	);

	/// <summary>
	/// Segment spelling corrections. Keys are the .NET-recommended (but English-incorrect) spellings;
	/// values are the correct-English acronym capitalization. Only acronyms that must remain uppercase
	/// are listed here; every other acronym-like word is exempt via <see cref="DefaultAllowedWords"/>.
	/// </summary>
	internal static readonly ImmutableDictionary<string, string> DefaultAcronymMap = ImmutableDictionary.CreateRange(
		StringComparer.Ordinal,
		new Dictionary<string, string>
		{
			["Api"] = "API",
			["Ai"] = "AI",
			["Ui"] = "UI",
			["Io"] = "IO",
			["Os"] = "OS",
			["Cpu"] = "CPU",
			["Gpu"] = "GPU",
			["Cli"] = "CLI",
			["Gui"] = "GUI",
			["Ram"] = "RAM",
			["Ssh"] = "SSH",
			["Dsl"] = "DSL",
		}
	);

	internal const string AllowedWordsOptionKey = "dotnet_analyzer_configuration.pds0004.allowed_words";
	internal const string AcronymMapOptionKey = "dotnet_analyzer_configuration.pds0004.acronym_map";
	internal const string AllowedIdentifiersOptionKey = "dotnet_analyzer_configuration.pds0004.allowed_identifiers";

	/// <summary>
	/// Whole identifiers exempt from acronym capitalization (brand names and their word-boundary
	/// prefixes, e.g. <c>CosmosDb</c> covers <c>CosmosDbServer</c>). Empty by default; opt in per repo.
	/// </summary>
	internal static readonly ImmutableArray<string> DefaultAllowedIdentifiers = [];

	internal static readonly char[] SemicolonSeparators = [';'];

	/// <summary>
	/// Returns the corrected identifier when any segment is not already correct English, otherwise <c>null</c>.
	/// A leading <c>I</c> interface prefix (e.g. <c>IApiClient</c>) is preserved around the correction.
	/// </summary>
	internal static string? CorrectIdentifier(string name, EnglishNamingConfig config)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}

		if (MatchesAllowedIdentifier(config.AllowedIdentifiers, name))
		{
			return null;
		}

		var stripInterfacePrefix = name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);
		var body = stripInterfacePrefix ? name.Substring(1) : name;

		var words = SplitIntoWords(body);
		var changed = false;
		for (var i = 0; i < words.Count; i++)
		{
			var word = words[i];
			if (config.AllowedWords.Contains(word))
			{
				continue;
			}

			if (config.AcronymMap.TryGetValue(word, out var replacement))
			{
				words[i] = replacement;
				changed = true;
			}
		}

		if (!changed)
		{
			return null;
		}

		var corrected = string.Concat(words);
		return stripInterfacePrefix ? "I" + corrected : corrected;
	}

	/// <summary>
	/// Splits an identifier into words at case boundaries, keeping acronym runs intact
	/// (<c>HTTPClient</c> → <c>HTTP</c>|<c>Client</c>, <c>OpenApi</c> → <c>Open</c>|<c>Api</c>).
	/// </summary>
	static List<string> SplitIntoWords(string name)
	{
		var words = new List<string>();
		var start = 0;
		for (var i = 1; i < name.Length; i++)
		{
			var current = name[i];
			if (!char.IsUpper(current))
			{
				continue;
			}

			var previous = name[i - 1];
			var isLowerBoundary = char.IsLower(previous) || char.IsDigit(previous);
			var isAcronymBoundary =
				char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1]) && i - start > 1;

			if (isLowerBoundary || isAcronymBoundary)
			{
				words.Add(name.Substring(start, i - start));
				start = i;
			}
		}

		words.Add(name.Substring(start));
		return words;
	}

	/// <summary>
	/// Returns <c>true</c> when the symbol's name is mandated by a contract — an interface implementation
	/// (explicit or implicit) or a base-class override — and therefore must not be renamed.
	/// </summary>
	internal static bool IsContractMember(ISymbol symbol)
	{
		return symbol switch
		{
			IMethodSymbol method when method.IsOverride || method.OverriddenMethod is not null => true,
			IMethodSymbol method => ImplementsInterfaceMember(method),
			IPropertySymbol property when property.IsOverride || property.OverriddenProperty is not null => true,
			IPropertySymbol property => ImplementsInterfaceMember(property),
			IEventSymbol @event when @event.IsOverride || @event.OverriddenEvent is not null => true,
			IEventSymbol @event => ImplementsInterfaceMember(@event),
			_ => false,
		};
	}

	static bool ImplementsInterfaceMember(ISymbol member)
	{
		if (
			member
			is IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 }
				or IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 }
				or IEventSymbol { ExplicitInterfaceImplementations.Length: > 0 }
		)
		{
			return true;
		}

		var containingType = member.ContainingType;
		if (containingType is null || containingType.AllInterfaces.Length == 0)
		{
			return false;
		}

		foreach (var interfaceType in containingType.AllInterfaces)
		{
			foreach (var interfaceMember in interfaceType.GetMembers(member.Name))
			{
				if (interfaceMember.Kind != member.Kind)
				{
					continue;
				}

				var implementation = containingType.FindImplementationForInterfaceMember(interfaceMember);
				if (SymbolEqualityComparer.Default.Equals(implementation, member))
				{
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Returns <c>true</c> when the identifier matches any allowed identifier, in the order listed:
	/// the first matching entry wins. An entry matches when it equals the identifier or is a
	/// word-boundary prefix of it (the character after the entry starts a new word), so
	/// <c>CosmosDb</c> covers <c>CosmosDbServer</c> but not <c>CosmosDbms</c>.
	/// </summary>
	static bool MatchesAllowedIdentifier(ImmutableArray<string> allowedIdentifiers, string name)
	{
		foreach (var identifier in allowedIdentifiers)
		{
			if (MatchesIdentifier(identifier, name))
			{
				return true;
			}
		}

		return false;
	}

	static bool MatchesIdentifier(string identifier, string name)
	{
		if (identifier.Length > name.Length)
		{
			return false;
		}

		if (!name.StartsWith(identifier, StringComparison.Ordinal))
		{
			return false;
		}

		if (name.Length == identifier.Length)
		{
			return true;
		}

		var next = name[identifier.Length];
		return char.IsUpper(next) || next == '_';
	}
}

/// <summary>
/// Effective PDS0004 configuration for a compilation, composed from the shipped defaults plus any
/// <c>.editorconfig</c> overrides. Config entries are merged with the defaults and take precedence,
/// so a repo can opt into renames the defaults exempt (e.g. <c>Sql:SQL</c>) or opt out of renames
/// the defaults apply (e.g. <c>allowed_words = Cli</c>) without re-declaring the full lists.
/// </summary>
sealed class EnglishNamingConfig
{
	EnglishNamingConfig(
		ImmutableDictionary<string, string> acronymMap,
		ImmutableHashSet<string> allowedWords,
		ImmutableArray<string> allowedIdentifiers
	)
	{
		AcronymMap = acronymMap;
		AllowedWords = allowedWords;
		AllowedIdentifiers = allowedIdentifiers.IsDefault ? [] : allowedIdentifiers;
	}

	public ImmutableDictionary<string, string> AcronymMap { get; }

	public ImmutableHashSet<string> AllowedWords { get; }

	public ImmutableArray<string> AllowedIdentifiers { get; }

	public static EnglishNamingConfig FromOptions(AnalyzerConfigOptions options)
	{
		var configAllowedWords = ImmutableHashSet<string>.Empty;
		if (options.TryGetValue(EnglishNamingHelper.AllowedWordsOptionKey, out var allowedRaw))
		{
			var parsed = SplitList(allowedRaw);
			if (parsed is not null)
			{
				configAllowedWords = parsed;
			}
		}

		var configAcronymMap = ImmutableDictionary<string, string>.Empty;
		if (options.TryGetValue(EnglishNamingHelper.AcronymMapOptionKey, out var mapRaw))
		{
			var parsed = ParseMap(mapRaw);
			if (parsed is not null)
			{
				configAcronymMap = parsed;
			}
		}

		var allowedIdentifiers = EnglishNamingHelper.DefaultAllowedIdentifiers;
		if (options.TryGetValue(EnglishNamingHelper.AllowedIdentifiersOptionKey, out var identifiersRaw))
		{
			allowedIdentifiers = SplitArray(identifiersRaw);
		}

		// Merge config over defaults, with precedence: config acronym map > config allowed words >
		// default acronym map > default allowed words. Explicit config entries override the defaults.
		var acronymMap = EnglishNamingHelper.DefaultAcronymMap;
		foreach (var pair in configAcronymMap)
		{
			acronymMap = acronymMap.SetItem(pair.Key, pair.Value);
		}

		var allowedWords = EnglishNamingHelper.DefaultAllowedWords.Union(configAllowedWords);

		// Opting out of a default rename: the user put a default-mapped word in allowed_words without
		// re-mapping it, so drop the default map entry and let the exemption stand.
		foreach (var word in configAllowedWords)
		{
			if (EnglishNamingHelper.DefaultAcronymMap.ContainsKey(word) && !configAcronymMap.ContainsKey(word))
			{
				acronymMap = acronymMap.Remove(word);
			}
		}

		// Words that end up in the effective map are renamed, so they must not also be exempt.
		foreach (var word in acronymMap.Keys)
		{
			allowedWords = allowedWords.Remove(word);
		}

		return new EnglishNamingConfig(acronymMap, allowedWords, allowedIdentifiers);
	}

	internal static EnglishNamingConfig Create(
		ImmutableDictionary<string, string> acronymMap,
		ImmutableHashSet<string> allowedWords,
		ImmutableArray<string> allowedIdentifiers = default
	)
	{
		return new EnglishNamingConfig(acronymMap, allowedWords, allowedIdentifiers);
	}

	static ImmutableHashSet<string>? SplitList(string value)
	{
		var segments = value
			.Split(EnglishNamingHelper.SemicolonSeparators, StringSplitOptions.RemoveEmptyEntries)
			.Select(static s => s.Trim())
			.Where(static s => s.Length > 0)
			.ToArray();

		return segments.Length > 0 ? ImmutableHashSet.Create(StringComparer.Ordinal, segments) : null;
	}

	static ImmutableArray<string> SplitArray(string value)
	{
		return
		[
			.. value
				.Split(EnglishNamingHelper.SemicolonSeparators, StringSplitOptions.RemoveEmptyEntries)
				.Select(static s => s.Trim())
				.Where(static s => s.Length > 0),
		];
	}

	static ImmutableDictionary<string, string>? ParseMap(string value)
	{
		var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
		foreach (
			var pair in value.Split(EnglishNamingHelper.SemicolonSeparators, StringSplitOptions.RemoveEmptyEntries)
		)
		{
			var index = pair.IndexOf(':');
			if (index <= 0 || index >= pair.Length - 1)
			{
				continue;
			}

			var key = pair.Substring(0, index).Trim();
			var replacement = pair.Substring(index + 1).Trim();
			if (key.Length > 0 && replacement.Length > 0)
			{
				builder[key] = replacement;
			}
		}

		return builder.Count > 0 ? builder.ToImmutable() : null;
	}
}
