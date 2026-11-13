using System.Text.Json;

namespace Purview.BuildSdk.PropertySchema;

/// <summary>One entry under <c>properties</c> in a <c>.buildschema.json</c> sidecar.</summary>
sealed record BuildSchemaProperty(
	string Name,
	string Description,
	string? DefaultValue,
	string? IntrinsicType,
	string? CustomTypeName,
	IReadOnlyList<string> Values,
	bool AllowUnknownValues
);

/// <summary>A parsed MSBuild Editor sidecar schema.</summary>
sealed class BuildSchemaDocument
{
	BuildSchemaDocument(string path, JsonElement root, IReadOnlyDictionary<string, BuildSchemaProperty> properties)
	{
		Path = path;
		Root = root;
		Properties = properties;
	}

	public string Path { get; }

	public JsonElement Root { get; }

	public IReadOnlyDictionary<string, BuildSchemaProperty> Properties { get; }

	public static BuildSchemaDocument Load(string path)
	{
		using var document = JsonDocument.Parse(File.ReadAllText(path));
		var root = document.RootElement.Clone();

		Dictionary<string, (IReadOnlyList<string> Values, bool AllowUnknown)> types = new(StringComparer.Ordinal);
		if (root.TryGetProperty("types", out var typesElement))
		{
			foreach (var type in typesElement.EnumerateObject())
				types[type.Name] = ReadValues(type.Value);
		}

		Dictionary<string, BuildSchemaProperty> properties = new(StringComparer.Ordinal);
		if (root.TryGetProperty("properties", out var propertiesElement))
		{
			foreach (var property in propertiesElement.EnumerateObject())
				properties[property.Name] = ReadProperty(property.Name, property.Value, types);
		}

		return new(path, root, properties);
	}

	static BuildSchemaProperty ReadProperty(
		string name,
		JsonElement element,
		Dictionary<string, (IReadOnlyList<string> Values, bool AllowUnknown)> types
	)
	{
		if (element.ValueKind == JsonValueKind.String)
			return new(name, element.GetString() ?? "", null, null, null, [], false);

		var description = element.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
		var defaultValue = element.TryGetProperty("defaultValue", out var v) ? v.GetString() : null;

		string? intrinsic = null;
		string? customName = null;
		IReadOnlyList<string> values = [];
		var allowUnknown = false;

		if (element.TryGetProperty("type", out var type))
		{
			if (type.ValueKind == JsonValueKind.String)
				intrinsic = type.GetString();
			else if (type.ValueKind == JsonValueKind.Object && type.TryGetProperty("$ref", out var reference))
			{
				customName = reference.GetString()?.Split('/').Last();
				if (customName is not null && types.TryGetValue(customName, out var shared))
					(values, allowUnknown) = shared;
			}
			else if (type.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
				(values, allowUnknown) = ReadValues(type);
		}

		return new(name, description, defaultValue, intrinsic, customName, values, allowUnknown);
	}

	static (IReadOnlyList<string> Values, bool AllowUnknown) ReadValues(JsonElement type)
	{
		if (type.ValueKind == JsonValueKind.Array)
			return ([.. type.EnumerateArray().Select(e => e.GetString() ?? "")], false);

		var values = type.TryGetProperty("values", out var v) ? v.EnumerateObject().Select(p => p.Name).ToArray() : [];
		var allowUnknown = type.TryGetProperty("allowUnknownValues", out var a) && a.GetBoolean();

		return (values, allowUnknown);
	}
}
