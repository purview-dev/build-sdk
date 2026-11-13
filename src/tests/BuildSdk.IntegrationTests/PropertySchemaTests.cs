using System.Text.Json;
using System.Text.RegularExpressions;
using Purview.BuildSdk.Harness;
using Purview.BuildSdk.PropertySchema;

namespace Purview.BuildSdk;

/// <summary>
/// Keeps every description of an SDK property in step with the code.
///
/// <c>Sdk/Sdk.props.buildschema.json</c> is the canonical description: MSBuild Editor reads it for
/// csproj IntelliSense, and these tests hold the code, the compiler-visible list, the README and the
/// wiki to it. Each failure names what to add and where.
///
/// Everything here reads files, except <see cref="SchemaDefault_EveryStatedDefault_MatchesTheEvaluatedDefault"/>,
/// which evaluates a throwaway project. Nothing touches the network.
/// </summary>
sealed partial class PropertySchemaTests
{
	/// <summary>The upstream commit <c>Schemas/buildschema.json</c> was copied from.</summary>
	public const string MetaSchemaCommit = "9720806e8fa80cd9c4cb31e12b01c63811f39a75";

	/// <summary>The sentence a description uses to say the property must be set before the import.</summary>
	public const string PreImportPhrase = "before the `Sdk.props` import";

	/// <summary>The sentence a description uses to explain why there is no <c>defaultValue</c>.</summary>
	public const string NoDefaultPhrase = "No default: ";

	const string SchemaFile = "src/src/BuildSdk/Sdk/Sdk.props.buildschema.json";

	static readonly Lazy<SdkPropertyInventory> LazyInventory = new(() =>
		SdkPropertyInventory.Load(SdkPaths.SdkDirectory)
	);

	static readonly Lazy<BuildSchemaDocument> LazySchema = new(() =>
		BuildSchemaDocument.Load(Path.Combine(SdkPaths.SdkDirectory, "Sdk.props.buildschema.json"))
	);

	static SdkPropertyInventory Inventory => LazyInventory.Value;

	static BuildSchemaDocument Schema => LazySchema.Value;

	[GeneratedRegex(@"""build_property\.(?<name>[A-Za-z_][A-Za-z0-9_]*)""")]
	private static partial Regex BuildPropertyReadPattern();

	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespacePattern();

	static readonly string[] AnalyzerProjects = ["Analyzers", "CodeFixers"];

	static string RepositoryRoot
	{
		get
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory is not null)
			{
				if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
					return directory.FullName;

				directory = directory.Parent;
			}

			throw new InvalidOperationException($"No AGENTS.md was found above '{AppContext.BaseDirectory}'.");
		}
	}

	static bool IsExcluded(string name) =>
		SdkPropertyClassification.ExternallyOwned.ContainsKey(name)
		|| SdkPropertyClassification.Internal.ContainsKey(name)
		|| SdkPropertyClassification.Aliases.ContainsKey(name);

	// ---------------------------------------------------------------------------------------------
	// Schema versus the shipped .props/.targets files
	// ---------------------------------------------------------------------------------------------

	[Test]
	public async Task Schema_EveryDefinedSdkProperty_HasAnEntry()
	{
		// Arrange
		var defined = Inventory.PublicDefinedNames.Where(n => !IsExcluded(n));

		// Act
		var missing = defined
			.Where(n => !Schema.Properties.ContainsKey(n))
			.Select(n =>
			{
				var definition = Inventory.FirstDefinition(n)!;
				return $"'{n}' is defined at Sdk/{definition.File}:{definition.Line} but has no entry in {SchemaFile}. "
					+ "Add it under \"properties\" with a description, type and defaultValue. If it is internal, "
					+ "prefix it with '_' or add it to SdkPropertyClassification.Internal with a reason.";
			})
			.ToArray();

		// Assert
		await Assert.That(missing).IsEmpty().Because(string.Join(Environment.NewLine, missing));
	}

	[Test]
	public async Task Schema_EveryEntry_IsDefinedOrReadByTheSdk()
	{
		// Arrange
		var entries = Schema.Properties.Keys;

		// Act
		var orphaned = entries
			.Where(n => !Inventory.IsDefinedOrRead(n))
			.Select(n =>
				$"{SchemaFile} describes '{n}', but no shipped .props/.targets file defines or reads it. "
				+ "Remove the entry, or define the property (with its default) in Sdk/Props/Defaults.props."
			)
			.ToArray();

		// Assert
		await Assert.That(orphaned).IsEmpty().Because(string.Join(Environment.NewLine, orphaned));
	}

	[Test]
	public async Task Schema_NoEntry_DescribesAnExcludedProperty()
	{
		// Arrange
		var entries = Schema.Properties.Keys;

		// Act
		var excluded = entries
			.Where(IsExcluded)
			.Select(n =>
				$"{SchemaFile} describes '{n}', which SdkPropertyClassification excludes. Remove one or the other."
			)
			.ToArray();

		// Assert
		await Assert.That(excluded).IsEmpty().Because(string.Join(Environment.NewLine, excluded));
	}

	[Test]
	public async Task Classification_EveryExclusion_NamesARealProperty()
	{
		// Arrange
		var exclusions = SdkPropertyClassification
			.ExternallyOwned.Keys.Concat(SdkPropertyClassification.Internal.Keys)
			.Concat(SdkPropertyClassification.Aliases.Keys);

		var exported = Inventory.CompilerVisibleProperties.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

		// Act
		var stale = exclusions
			.Where(n => !Inventory.IsDefinedOrRead(n) && !exported.Contains(n))
			.Select(n =>
				$"SdkPropertyClassification excludes '{n}', but no shipped file defines or reads it any more. Remove it from the list."
			)
			.ToArray();

		// Assert
		await Assert.That(stale).IsEmpty().Because(string.Join(Environment.NewLine, stale));
	}

	// ---------------------------------------------------------------------------------------------
	// Compiler-visible properties
	// ---------------------------------------------------------------------------------------------

	[Test]
	public async Task CompilerVisibleProperty_EveryItem_HasASchemaEntry()
	{
		// Arrange
		var items = Inventory.CompilerVisibleProperties;

		// Act
		var missing = items
			.Where(i => !Schema.Properties.ContainsKey(i.Name) && !IsExcluded(i.Name))
			.Select(i =>
				$"'{i.Name}' is exported as a CompilerVisibleProperty at Sdk/{i.File}:{i.Line} but has no entry in {SchemaFile}. "
				+ "Add one, so a consumer's analyzer author can find out what build_property."
				+ i.Name
				+ " means."
			)
			.ToArray();

		// Assert
		await Assert.That(missing).IsEmpty().Because(string.Join(Environment.NewLine, missing));
	}

	[Test]
	public async Task CompilerVisibleProperty_EveryItem_IsDefinedOrRead()
	{
		// Arrange
		var items = Inventory.CompilerVisibleProperties;

		// Act
		var undefined = items
			.Where(i =>
				!Inventory.IsDefinedOrRead(i.Name) && !SdkPropertyClassification.ExternallyOwned.ContainsKey(i.Name)
			)
			.Select(i =>
				$"'{i.Name}' is exported as a CompilerVisibleProperty at Sdk/{i.File}:{i.Line}, but no shipped file defines or reads it, "
				+ "so build_property."
				+ i.Name
				+ " is always empty. Define it, or remove the CompilerVisibleProperty item."
			)
			.ToArray();

		// Assert
		await Assert.That(undefined).IsEmpty().Because(string.Join(Environment.NewLine, undefined));
	}

	[Test]
	public async Task BuildPropertyRead_EveryAnalyzerRead_IsCompilerVisible()
	{
		// Arrange
		var sources = AnalyzerProjects
			.Select(p => Path.Combine(RepositoryRoot, "src", "src", p))
			.SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
			.Where(f =>
				!f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
			)
			.Where(f =>
				!f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
			);
		var exported = Inventory.CompilerVisibleProperties.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

		// Act
		var reads = sources
			.SelectMany(f =>
				BuildPropertyReadPattern()
					.Matches(File.ReadAllText(f))
					.Select(m =>
						(File: Path.GetRelativePath(RepositoryRoot, f).Replace('\\', '/'), m.Groups["name"].Value)
					)
			)
			.ToArray();
		var unexported = reads
			.Where(r => !exported.Contains(r.Value))
			.Select(r =>
				$"{r.File} reads build_property.{r.Value}, but Sdk.props exports no CompilerVisibleProperty '{r.Value}', "
				+ "so the analyzer always sees it unset. Add <CompilerVisibleProperty Include=\""
				+ r.Value
				+ "\"> (with a <Description>) to the compiler-visible ItemGroup in Sdk/Sdk.props."
			)
			.ToArray();

		// Assert
		await Assert
			.That(reads)
			.IsNotEmpty()
			.Because("The scan found no build_property reads at all; the pattern is broken.");
		await Assert.That(unexported).IsEmpty().Because(string.Join(Environment.NewLine, unexported));
	}

	/// <summary>
	/// The static list is what the other tests read, so it must be what MSBuild actually registers. Most
	/// items apply to every C# project; the Roslyn compiler settings only to a Roslyn component, so both
	/// shapes are evaluated.
	/// </summary>
	[Test]
	public async Task CompilerVisibleProperty_EveryStaticItem_IsRegisteredForSomeCSharpProject(
		CancellationToken cancellationToken
	)
	{
		// Arrange
		using var library = await ProjectHarness.CreateAsync("MyLibrary", cancellationToken: cancellationToken);
		using var component = await ProjectHarness
			.For("SourceGeneration")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
					<PropertyGroup>
						<IsRoslynComponent>true</IsRoslynComponent>
					</PropertyGroup>
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);
		var declared = Inventory
			.CompilerVisibleProperties.Select(i => i.Name)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		// Act
		var registered = (await library.GetItemIdentitiesAsync("CompilerVisibleProperty", cancellationToken))
			.Concat(await component.GetItemIdentitiesAsync("CompilerVisibleProperty", cancellationToken))
			.ToHashSet(StringComparer.Ordinal);
		var missing = declared.Where(n => !registered.Contains(n)).ToArray();

		// Assert
		await Assert
			.That(missing)
			.IsEmpty()
			.Because(
				"These CompilerVisibleProperty items are declared in the SDK but registered for neither a class library nor a Roslyn component: "
					+ string.Join(", ", missing)
			);
	}

	// ---------------------------------------------------------------------------------------------
	// What the schema claims about each property
	// ---------------------------------------------------------------------------------------------

	[Test]
	public async Task SchemaDefault_EveryStatedDefault_MatchesTheEvaluatedDefault(CancellationToken cancellationToken)
	{
		// Arrange
		using var h = await ProjectHarness
			.For("MyLibrary")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);
		var stated = Schema
			.Properties.Values.Where(p => p.DefaultValue is not null)
			.Where(p => !SdkPropertyClassification.SetByTheHarness.ContainsKey(p.Name))
			.ToArray();

		// Act
		var evaluated = await h.GetPropertiesAsync(cancellationToken, [.. stated.Select(p => p.Name)]);
		var wrong = stated
			.Where(p =>
				!string.Equals(Normalise(evaluated[p.Name]), Normalise(p.DefaultValue!), StringComparison.Ordinal)
			)
			.Select(p =>
				$"{SchemaFile} states defaultValue '{p.DefaultValue}' for '{p.Name}', but a plain class library evaluates it to "
				+ $"'{evaluated[p.Name]}'. Update the schema's defaultValue (and the README and wiki rows) to the real default."
			)
			.ToArray();

		// Assert
		await Assert.That(wrong).IsEmpty().Because(string.Join(Environment.NewLine, wrong));

		static string Normalise(string value) => WhitespacePattern().Replace(value.Trim(), "");
	}

	[Test]
	public async Task SchemaDefault_EveryEntryWithoutADefault_IsEmptyOrExplained(CancellationToken cancellationToken)
	{
		// Arrange
		using var h = await ProjectHarness
			.For("MyLibrary")
			.WithProjectFileContent(
				"""
				<Project Sdk="Microsoft.NET.Sdk">
				</Project>
				"""
			)
			.BuildAsync(cancellationToken);
		var unstated = Schema.Properties.Values.Where(p => p.DefaultValue is null).ToArray();

		// Act
		var evaluated = await h.GetPropertiesAsync(cancellationToken, [.. unstated.Select(p => p.Name)]);
		var unexplained = unstated
			.Where(p =>
				evaluated[p.Name].Trim().Length > 0 && !SdkPropertyClassification.ComputedDefaults.ContainsKey(p.Name)
			)
			.Select(p =>
				$"'{p.Name}' has no defaultValue in {SchemaFile}, but a plain class library evaluates it to '{evaluated[p.Name]}'. "
				+ "State that as its defaultValue, or - if the value depends on the machine or the project - add it to "
				+ "SdkPropertyClassification.ComputedDefaults with the reason."
			)
			.ToArray();
		var contradicted = Schema
			.Properties.Values.Where(p =>
				p.DefaultValue is not null && SdkPropertyClassification.ComputedDefaults.ContainsKey(p.Name)
			)
			.Select(p =>
				$"'{p.Name}' is listed in SdkPropertyClassification.ComputedDefaults but states a defaultValue. Remove one."
			)
			.ToArray();

		// Assert
		await Assert.That(unexplained).IsEmpty().Because(string.Join(Environment.NewLine, unexplained));
		await Assert.That(contradicted).IsEmpty().Because(string.Join(Environment.NewLine, contradicted));
	}

	[Test]
	public async Task SchemaDescription_EveryEntryWithoutADefault_SaysWhy()
	{
		// Arrange
		var properties = Schema.Properties.Values;

		// Act
		var unexplained = properties
			.Where(p => p.DefaultValue is null && !p.Description.Contains(NoDefaultPhrase, StringComparison.Ordinal))
			.Select(p =>
				$"'{p.Name}' has no defaultValue, so its description in {SchemaFile} must say why, in a sentence starting '{NoDefaultPhrase}'."
			);
		var contradicted = properties
			.Where(p => p.DefaultValue is not null && p.Description.Contains(NoDefaultPhrase, StringComparison.Ordinal))
			.Select(p =>
				$"'{p.Name}' states defaultValue '{p.DefaultValue}' and also says '{NoDefaultPhrase}'. Remove one."
			);
		var problems = unexplained.Concat(contradicted).ToArray();

		// Assert
		await Assert.That(problems).IsEmpty().Because(string.Join(Environment.NewLine, problems));
	}

	[Test]
	public async Task SchemaDescription_PreImportClaim_MatchesTheSdkPropsReads()
	{
		// Arrange
		var properties = Schema.Properties.Values;

		// Act
		var unclaimed = properties
			.Where(p =>
				Inventory.PropsReads.ContainsKey(p.Name)
				&& !p.Description.Contains(PreImportPhrase, StringComparison.Ordinal)
			)
			.Select(p =>
				$"'{p.Name}' is read while Sdk.props is evaluated (Sdk/{Inventory.PropsReads[p.Name]}), so a value set in a project "
				+ $"file comes too late. Say so in its description in {SchemaFile}: \"Set it {PreImportPhrase}.\""
			);
		var overclaimed = properties
			.Where(p =>
				!Inventory.PropsReads.ContainsKey(p.Name)
				&& p.Description.Contains(PreImportPhrase, StringComparison.Ordinal)
			)
			.Select(p =>
				$"The description of '{p.Name}' in {SchemaFile} says to set it {PreImportPhrase}, but nothing in Sdk.props or the "
				+ "files it imports reads it, so a project file can set it too. Remove the claim."
			);
		var problems = unclaimed.Concat(overclaimed).ToArray();

		// Assert
		await Assert.That(problems).IsEmpty().Because(string.Join(Environment.NewLine, problems));
	}

	[Test]
	public async Task SchemaType_EveryBooleanDefault_IsTrueOrFalse()
	{
		// Arrange
		var booleans = Schema.Properties.Values.Where(p => p.IntrinsicType == "bool" && p.DefaultValue is not null);

		// Act
		var invalid = booleans
			.Where(p => p.DefaultValue is not ("true" or "false"))
			.Select(p => $"'{p.Name}' is typed bool in {SchemaFile} but its defaultValue is '{p.DefaultValue}'.")
			.ToArray();

		// Assert
		await Assert.That(invalid).IsEmpty().Because(string.Join(Environment.NewLine, invalid));
	}

	[Test]
	public async Task SchemaType_EveryEnumeration_IsRecognisedByTheSdk()
	{
		// Arrange
		var shipped = string.Join(
			"\n",
			Directory
				.EnumerateFiles(SdkPaths.SdkDirectory, "*.*", SearchOption.AllDirectories)
				.Where(f =>
					f.EndsWith(".props", StringComparison.Ordinal) || f.EndsWith(".targets", StringComparison.Ordinal)
				)
				.Select(File.ReadAllText)
		);
		var enumerations = Schema.Properties.Values.Where(p => p.Values.Count > 0 && !p.AllowUnknownValues).ToArray();

		// Act
		var unrecognised = enumerations
			.SelectMany(p => p.Values.Select(v => (Property: p, Value: v)))
			.Where(x => !shipped.Contains(x.Value, StringComparison.OrdinalIgnoreCase))
			.Select(x =>
				$"{SchemaFile} offers '{x.Value}' for '{x.Property.Name}', but no shipped file mentions that value, so the SDK cannot act on it."
			);
		var defaultsOutsideTheList = enumerations
			.Where(p =>
				p.DefaultValue is not null && !p.Values.Contains(p.DefaultValue, StringComparer.OrdinalIgnoreCase)
			)
			.Select(p => $"'{p.Name}' defaults to '{p.DefaultValue}', which is not one of its values in {SchemaFile}.");
		var problems = unrecognised.Concat(defaultsOutsideTheList).ToArray();

		// Assert
		await Assert.That(enumerations).IsNotEmpty();
		await Assert.That(problems).IsEmpty().Because(string.Join(Environment.NewLine, problems));
	}

	// ---------------------------------------------------------------------------------------------
	// The meta-schema
	// ---------------------------------------------------------------------------------------------

	[Test]
	public async Task Schema_ValidatesAgainstTheMetaSchema()
	{
		// Arrange
		var validator = new DraftFourValidator(LoadMetaSchema());

		// Act
		var errors = validator.Validate(Schema.Root);

		// Assert
		await Assert.That(errors).IsEmpty().Because(string.Join(Environment.NewLine, errors));
	}

	[Test]
	public async Task Schema_DeclaresTheMetaSchema()
	{
		// Arrange
		var root = Schema.Root;

		// Act
		var declared = root.TryGetProperty("$schema", out var value) ? value.GetString() : null;

		// Assert
		await Assert
			.That(declared)
			.IsEqualTo(
				"https://github.com/mhutch/MonoDevelop.MSBuildEditor/raw/main/MonoDevelop.MSBuild/Schemas/buildschema.json"
			);
	}

	[Test]
	public async Task MetaSchema_DeclaresDraftFour()
	{
		// Arrange
		var metaSchema = LoadMetaSchema();

		// Act
		var draft = metaSchema.GetProperty("$schema").GetString();

		// Assert
		await Assert.That(draft).IsEqualTo("http://json-schema.org/draft-04/schema#");
	}

	[Test]
	public async Task MetaSchemaValidator_ImplementsEveryKeywordTheMetaSchemaUses()
	{
		// Arrange
		var metaSchema = LoadMetaSchema();

		// Act
		var unsupported = DraftFourValidator.UnsupportedKeywords(metaSchema);

		// Assert
		await Assert
			.That(unsupported)
			.IsEmpty()
			.Because(
				"The vendored meta-schema uses keywords DraftFourValidator does not implement, so validation would pass without checking them: "
					+ string.Join(", ", unsupported)
			);
	}

	[Test]
	public async Task MetaSchemaValidator_InvalidSchema_IsRejected()
	{
		// Arrange
		var validator = new DraftFourValidator(LoadMetaSchema());
		using var invalid = JsonDocument.Parse(
			"""
			{ "properties": { "Foo": { "description": "x", "type": "not-a-type", "unknownKey": true } } }
			"""
		);

		// Act
		var errors = validator.Validate(invalid.RootElement);

		// Assert
		await Assert.That(errors).IsNotEmpty();
	}

	static JsonElement LoadMetaSchema()
	{
		var path = Path.Combine(
			RepositoryRoot,
			"src",
			"tests",
			"BuildSdk.IntegrationTests",
			"Schemas",
			"buildschema.json"
		);
		using var document = JsonDocument.Parse(File.ReadAllText(path));
		return document.RootElement.Clone();
	}

	// ---------------------------------------------------------------------------------------------
	// README and wiki tables
	// ---------------------------------------------------------------------------------------------

	/// <summary>Every markdown file whose property tables are held to the schema.</summary>
	public static IEnumerable<string> DocumentsWithPropertyTables() =>
		[
			"README.md",
			.. Directory
				.EnumerateFiles(Path.Combine(RepositoryRoot, "docs", "wiki"), "*.md", SearchOption.AllDirectories)
				.Select(f => Path.GetRelativePath(RepositoryRoot, f).Replace('\\', '/'))
				.Order(StringComparer.Ordinal),
		];

	/// <summary>The documents that must list every property: the README and the wiki's reference page.</summary>
	public static IEnumerable<string> ReferenceDocuments() => ["README.md", "docs/wiki/Configuration-Reference.md"];

	static IReadOnlyList<MarkdownPropertyRow> Rows(string document) =>
		MarkdownPropertyTables.Read(Path.Combine(RepositoryRoot, document), document);

	[Test]
	[MethodDataSource(nameof(ReferenceDocuments))]
	public async Task Documentation_ConfigurationTables_ListEverySchemaProperty(string document)
	{
		// Arrange
		var documented = Rows(document)
			.Where(r => r.DefaultCell is not null)
			.Select(r => r.Name)
			.ToHashSet(StringComparer.Ordinal);

		// Act
		var missing = Schema
			.Properties.Keys.Where(n => !documented.Contains(n))
			.Order(StringComparer.Ordinal)
			.Select(n =>
				$"{document} has no configuration-table row for '{n}'. Add `| `{n}` | <default> | <description> |` to the table for its area "
				+ $"(a table with a Default column), using the defaultValue from {SchemaFile}."
			)
			.ToArray();

		// Assert
		await Assert.That(missing).IsEmpty().Because(string.Join(Environment.NewLine, missing));
	}

	[Test]
	[MethodDataSource(nameof(DocumentsWithPropertyTables))]
	public async Task Documentation_EveryStatedDefault_MatchesTheSchema(string document)
	{
		// Arrange
		var rows = Rows(document).Where(r => r.DefaultCell is not null && Schema.Properties.ContainsKey(r.Name));

		// Act
		var wrong = rows.Select(r =>
				(Row: r, Stated: StatedLiteral(r.DefaultCell), Schema: Schema.Properties[r.Name].DefaultValue)
			)
			.Where(x => !Agrees(x.Stated, x.Schema))
			.Select(x =>
				x.Schema is null
					? $"{x.Row.File}:{x.Row.Line} states default `{x.Stated}` for '{x.Row.Name}', but {SchemaFile} gives it no default. "
						+ "Describe it in italics instead, for example *(detected)*, or add the defaultValue to the schema."
					: $"{x.Row.File}:{x.Row.Line} states default {(x.Stated is null ? x.Row.DefaultCell : $"`{x.Stated}`")} for '{x.Row.Name}', "
						+ $"but {SchemaFile} says `{x.Schema}`. Make the row open with `{MarkdownPropertyTables.NormaliseList(x.Schema)}`."
			)
			.ToArray();

		// Assert
		await Assert.That(wrong).IsEmpty().Because(string.Join(Environment.NewLine, wrong));

		static string? StatedLiteral(string? cell) =>
			MarkdownPropertyTables.StatedDefault(cell) is { } literal
			&& !literal.Contains('<', StringComparison.Ordinal)
				? literal
				: null;

		static bool Agrees(string? stated, string? schema) =>
			(stated, schema) switch
			{
				(null, null) => true,
				(null, _) or (_, null) => false,
				_ => string.Equals(
					MarkdownPropertyTables.NormaliseList(stated),
					MarkdownPropertyTables.NormaliseList(schema),
					StringComparison.Ordinal
				),
			};
	}

	[Test]
	[MethodDataSource(nameof(DocumentsWithPropertyTables))]
	public async Task Documentation_EveryPropertyRow_NamesARealProperty(string document)
	{
		// Arrange
		var rows = Rows(document);

		// Act
		// A '_'-prefixed name is private by convention; a page explaining internals may still list one.
		var unknown = rows.Where(r =>
				r.Name[0] != '_'
				&& !Schema.Properties.ContainsKey(r.Name)
				&& !IsExcluded(r.Name)
				&& !SdkPropertyClassification.DocumentedNonProperties.ContainsKey(r.Name)
			)
			.Select(r =>
				$"{r.File}:{r.Line} documents '{r.Name}', which is neither in {SchemaFile} nor a known external property. "
				+ "Fix the name, remove the row, or add the property to the schema."
			)
			.ToArray();

		// Assert
		await Assert.That(unknown).IsEmpty().Because(string.Join(Environment.NewLine, unknown));
	}

	[Test]
	[MethodDataSource(nameof(ReferenceDocuments))]
	public async Task Documentation_CompilerVisibleTable_ListsEveryCompilerVisibleProperty(string document)
	{
		// Arrange
		var table = Rows(document)
			.Where(r => r.Section == MarkdownPropertyTables.CompilerVisibleSection)
			.Select(r => r.Name)
			.ToHashSet(StringComparer.Ordinal);
		var exported = Inventory.CompilerVisibleProperties.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

		// Act
		var missing = exported
			.Where(n => !table.Contains(n))
			.Order(StringComparer.Ordinal)
			.Select(n =>
				$"{document} does not list the compiler-visible property '{n}'. Add `| `{n}` | <description> |` to its "
				+ $"'{MarkdownPropertyTables.CompilerVisibleSection}' table."
			);
		var extra = table
			.Where(n => !exported.Contains(n))
			.Order(StringComparer.Ordinal)
			.Select(n =>
				$"{document} lists '{n}' as compiler-visible, but Sdk.props exports no CompilerVisibleProperty '{n}'. Remove the row."
			);
		var problems = missing.Concat(extra).ToArray();

		// Assert
		await Assert.That(table).IsNotEmpty();
		await Assert.That(problems).IsEmpty().Because(string.Join(Environment.NewLine, problems));
	}
}
