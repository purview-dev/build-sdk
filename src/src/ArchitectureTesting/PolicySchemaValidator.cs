using System.Text.Json;
using Json.Schema;

namespace Purview.Architecture.Testing;

/// <summary>
/// Validates a policy document against the schema embedded in this package.
/// </summary>
/// <remarks>
/// The schema is embedded rather than fetched, so validation needs no network and cannot drift from
/// the reader in this same package. The identical file is published at <see cref="SchemaId"/> for
/// editors, which is what a policy's <c>$schema</c> points at.
/// </remarks>
public static class PolicySchemaValidator
{
	const string SchemaResourceName = "Purview.Architecture.Testing.PolicySchema.json";

	/// <summary>The published identifier a policy file's <c>$schema</c> should reference.</summary>
	public const string SchemaId =
		"https://raw.githubusercontent.com/purview-dev/build-sdk/main/architecture-policy.schema.json";

	/// <summary>
	/// The parsed schema.
	/// </summary>
	/// <remarks>
	/// <c>JsonSchema.FromText</c> registers a schema globally by its <c>$id</c>, and a second
	/// registration of the same identifier throws. Parsing once is required, not just an optimisation.
	/// </remarks>
	static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

	/// <summary>Validates a policy document.</summary>
	/// <param name="policyPath">The path to the policy file.</param>
	/// <returns>The failures, each naming the instance location that broke. Empty when valid.</returns>
	public static IReadOnlyList<string> Validate(string policyPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(policyPath);

		if (!File.Exists(policyPath))
			return [$"The architecture policy was not found at '{policyPath}'."];

		using var document = JsonDocument.Parse(
			File.ReadAllText(policyPath),
			// A policy may carry comments and trailing commas; an editor tolerates both, so validation
			// must too, or the file is valid in one tool and broken in another.
			new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }
		);

		var results = Schema.Value.Evaluate(
			document.RootElement,
			// Hierarchical carries the failing keyword and instance location; the default output says
			// only valid or invalid, which is useless for a file this size.
			new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical, RequireFormatValidation = true }
		);

		return results.IsValid
			? []
			:
			[
				.. Leaves(results)
					.SelectMany(leaf => leaf.Errors?.Select(error => $"{Location(leaf)}: {error.Value}") ?? []),
			];
	}

	/// <summary>Reads the embedded schema.</summary>
	/// <returns>The schema.</returns>
	static JsonSchema LoadSchema()
	{
		using var stream =
			typeof(PolicySchemaValidator).Assembly.GetManifestResourceStream(SchemaResourceName)
			?? throw new InvalidOperationException($"The embedded policy schema '{SchemaResourceName}' was not found.");
		using StreamReader reader = new(stream);

		return JsonSchema.FromText(reader.ReadToEnd());
	}

	/// <summary>
	/// The failing results with no failing children, so a container failure reports the key that
	/// caused it rather than a generic message about the object.
	/// </summary>
	/// <param name="results">The results to walk.</param>
	/// <returns>The failing leaves.</returns>
	static IEnumerable<EvaluationResults> Leaves(EvaluationResults results)
	{
		var children = (results.Details ?? []).Where(detail => !detail.IsValid).ToList();

		if (children.Count == 0)
		{
			if (!results.IsValid)
				yield return results;

			yield break;
		}

		foreach (var child in children)
		{
			foreach (var leaf in Leaves(child))
				yield return leaf;
		}
	}

	/// <summary>Renders an instance location as a readable path.</summary>
	/// <param name="result">The failing result.</param>
	/// <returns>The location.</returns>
	static string Location(EvaluationResults result)
	{
		var pointer = result.InstanceLocation.ToString();

		return string.IsNullOrEmpty(pointer) ? "(root)" : pointer.TrimStart('/').Replace('/', '.');
	}
}
