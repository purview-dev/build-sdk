using Microsoft.CodeAnalysis.Diagnostics;

namespace Purview.BuildSdk.Analyzers;

/// <summary>
/// Analyzer-config property names the SDK exposes to Roslyn as <c>build_property.*</c>.
/// </summary>
static class BuildPropertyKeys
{
	public const string ProjectDir = "build_property.ProjectDir";
}

/// <summary>
/// Read helpers for <see cref="AnalyzerConfigOptions"/>.
/// </summary>
static class AnalyzerConfigOptionsExtensions
{
	public static bool TryGetBuildProperty(this AnalyzerConfigOptions options, string key, out string value)
	{
		if (options.TryGetValue(key, out var configuredValue) && !string.IsNullOrWhiteSpace(configuredValue))
		{
			value = configuredValue;
			return true;
		}

		value = string.Empty;
		return false;
	}
}
