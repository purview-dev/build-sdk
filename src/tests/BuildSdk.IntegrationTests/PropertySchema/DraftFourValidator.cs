using System.Text.Json;
using System.Text.RegularExpressions;

namespace Purview.BuildSdk.PropertySchema;

/// <summary>
/// A JSON Schema validator for exactly the keywords the vendored MSBuild Editor meta-schema uses.
/// </summary>
/// <remarks>
/// Hand-rolled rather than a package so the check needs no dependency and no network. It is kept
/// honest by <see cref="UnsupportedKeywords"/>: a meta-schema that starts using a keyword this class
/// does not implement fails a test, instead of being validated silently and incompletely.
/// <c>const</c> is supported although it is not draft-04, because the meta-schema uses it.
/// </remarks>
sealed class DraftFourValidator(JsonElement root)
{
	/// <summary>Keywords that constrain an instance, and are implemented below.</summary>
	public static readonly IReadOnlySet<string> AssertionKeywords = new HashSet<string>(StringComparer.Ordinal)
	{
		"$ref",
		"additionalProperties",
		"allOf",
		"anyOf",
		"const",
		"enum",
		"items",
		"minLength",
		"not",
		"oneOf",
		"pattern",
		"patternProperties",
		"properties",
		"required",
		"type",
	};

	/// <summary>Keywords that describe rather than constrain, so ignoring them is correct.</summary>
	public static readonly IReadOnlySet<string> AnnotationKeywords = new HashSet<string>(StringComparer.Ordinal)
	{
		"$comment",
		"$id",
		"$schema",
		"default",
		"definitions",
		"description",
		"format",
		"title",
	};

	/// <summary>Every keyword in a schema position of <paramref name="schema"/> this class does not know.</summary>
	public static IReadOnlyList<string> UnsupportedKeywords(JsonElement schema)
	{
		SortedSet<string> unsupported = new(StringComparer.Ordinal);
		Visit(schema);
		return [.. unsupported];

		void Visit(JsonElement node)
		{
			if (node.ValueKind != JsonValueKind.Object)
				return;

			foreach (var keyword in node.EnumerateObject())
			{
				if (!AssertionKeywords.Contains(keyword.Name) && !AnnotationKeywords.Contains(keyword.Name))
					unsupported.Add(keyword.Name);

				if (keyword.Name is "properties" or "patternProperties" or "definitions")
				{
					foreach (var child in keyword.Value.EnumerateObject())
						Visit(child.Value);
				}
				else if (keyword.Name is "allOf" or "anyOf" or "oneOf")
				{
					foreach (var child in keyword.Value.EnumerateArray())
						Visit(child);
				}
				else if (keyword.Name is "items" or "not" or "additionalProperties")
					Visit(keyword.Value);
			}
		}
	}

	public IReadOnlyList<string> Validate(JsonElement instance)
	{
		List<string> errors = [];
		Validate(root, instance, "$", errors);
		return errors;
	}

	void Validate(JsonElement schema, JsonElement instance, string path, List<string> errors)
	{
		if (schema.ValueKind == JsonValueKind.True)
			return;

		if (schema.ValueKind == JsonValueKind.False)
		{
			errors.Add($"{path}: is not allowed here.");
			return;
		}

		if (schema.TryGetProperty("$ref", out var reference))
		{
			Validate(Resolve(reference.GetString()!), instance, path, errors);
			return;
		}

		if (schema.TryGetProperty("type", out var type) && !MatchesType(type, instance))
		{
			errors.Add($"{path}: expected {type.GetRawText()}, found {instance.ValueKind}.");
			return;
		}

		ValidateValue(schema, instance, path, errors);

		if (instance.ValueKind == JsonValueKind.Object)
			ValidateObject(schema, instance, path, errors);

		if (instance.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
		{
			var index = 0;
			foreach (var item in instance.EnumerateArray())
				Validate(items, item, $"{path}[{index++}]", errors);
		}

		ValidateCombinators(schema, instance, path, errors);
	}

	static void ValidateValue(JsonElement schema, JsonElement instance, string path, List<string> errors)
	{
		if (schema.TryGetProperty("const", out var constant) && !JsonEquals(constant, instance))
			errors.Add($"{path}: must be {constant.GetRawText()}.");

		if (
			schema.TryGetProperty("enum", out var enumeration)
			&& !enumeration.EnumerateArray().Any(e => JsonEquals(e, instance))
		)
			errors.Add($"{path}: {instance.GetRawText()} is not one of {enumeration.GetRawText()}.");

		if (instance.ValueKind != JsonValueKind.String)
			return;

		var text = instance.GetString()!;
		if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(text, pattern.GetString()!))
			errors.Add($"{path}: '{text}' does not match /{pattern.GetString()}/.");

		if (schema.TryGetProperty("minLength", out var minLength) && text.Length < minLength.GetInt32())
			errors.Add($"{path}: must be at least {minLength.GetInt32()} characters.");
	}

	void ValidateCombinators(JsonElement schema, JsonElement instance, string path, List<string> errors)
	{
		if (schema.TryGetProperty("allOf", out var allOf))
		{
			foreach (var branch in allOf.EnumerateArray())
				Validate(branch, instance, path, errors);
		}

		if (
			schema.TryGetProperty("anyOf", out var anyOf)
			&& !anyOf.EnumerateArray().Any(b => IsValid(b, instance, path))
		)
			errors.Add($"{path}: matches none of the anyOf alternatives.");

		if (schema.TryGetProperty("oneOf", out var oneOf))
		{
			var matches = oneOf.EnumerateArray().Count(b => IsValid(b, instance, path));
			if (matches != 1)
				errors.Add($"{path}: matches {matches} of the oneOf alternatives; exactly one is required.");
		}

		if (schema.TryGetProperty("not", out var not) && IsValid(not, instance, path))
			errors.Add($"{path}: matches a schema it must not match.");
	}

	void ValidateObject(JsonElement schema, JsonElement instance, string path, List<string> errors)
	{
		if (schema.TryGetProperty("required", out var required))
		{
			foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
			{
				if (!instance.TryGetProperty(name, out _))
					errors.Add($"{path}: is missing the required '{name}'.");
			}
		}

		var hasProperties = schema.TryGetProperty("properties", out var properties);
		var hasPatterns = schema.TryGetProperty("patternProperties", out var patterns);
		var hasAdditional = schema.TryGetProperty("additionalProperties", out var additional);

		foreach (var member in instance.EnumerateObject())
		{
			var memberPath = $"{path}.{member.Name}";
			var matched = false;

			if (hasProperties && properties.TryGetProperty(member.Name, out var propertySchema))
			{
				matched = true;
				Validate(propertySchema, member.Value, memberPath, errors);
			}

			if (hasPatterns)
			{
				foreach (var pattern in patterns.EnumerateObject())
				{
					if (!Regex.IsMatch(member.Name, pattern.Name))
						continue;

					matched = true;
					Validate(pattern.Value, member.Value, memberPath, errors);
				}
			}

			if (!matched && hasAdditional)
				Validate(additional, member.Value, memberPath, errors);
		}
	}

	bool IsValid(JsonElement schema, JsonElement instance, string path)
	{
		List<string> scratch = [];
		Validate(schema, instance, path, scratch);
		return scratch.Count == 0;
	}

	JsonElement Resolve(string reference)
	{
		if (!reference.StartsWith("#/", StringComparison.Ordinal))
			throw new NotSupportedException($"Only local references are supported, not '{reference}'.");

		var node = root;
		foreach (var segment in reference[2..].Split('/'))
			node = node.GetProperty(
				segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)
			);

		return node;
	}

	static bool MatchesType(JsonElement type, JsonElement instance) =>
		type.ValueKind == JsonValueKind.Array
			? type.EnumerateArray().Any(t => MatchesType(t.GetString()!, instance))
			: MatchesType(type.GetString()!, instance);

	static bool MatchesType(string type, JsonElement instance) =>
		type switch
		{
			"object" => instance.ValueKind == JsonValueKind.Object,
			"array" => instance.ValueKind == JsonValueKind.Array,
			"string" => instance.ValueKind == JsonValueKind.String,
			"boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
			"number" => instance.ValueKind == JsonValueKind.Number,
			"integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
			"null" => instance.ValueKind == JsonValueKind.Null,
			_ => throw new NotSupportedException($"Unknown JSON Schema type '{type}'."),
		};

	static bool JsonEquals(JsonElement left, JsonElement right) => JsonElement.DeepEquals(left, right);
}
