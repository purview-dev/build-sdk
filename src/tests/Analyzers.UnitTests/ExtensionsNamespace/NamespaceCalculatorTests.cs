namespace Purview.BuildSdk.Analyzers.ExtensionsNamespace;

/// <summary>
/// Unit tests for <see cref="ExtensionsNamespaceHelper"/> namespace derivation logic.
/// </summary>
[Category("Unit")]
public sealed class NamespaceCalculatorTests
{
	[Test]
	[Arguments(
		@"C:\repo\MyProject\",
		@"C:\repo\MyProject\Extensions\System\StringExtensions.cs",
		"System",
		DisplayName = "Single level under Extensions → namespace matches folder name"
	)]
	[Arguments(
		@"C:\repo\MyProject\",
		@"C:\repo\MyProject\Extensions\Microsoft\Extensions\Configuration\ConfigExt.cs",
		"Microsoft.Extensions.Configuration",
		DisplayName = "Nested Extensions sub-folder → full path becomes namespace"
	)]
	[Arguments(
		@"C:\repo\MyProject\",
		@"C:\repo\MyProject\Extensions\MyLib\Foo\Bar.cs",
		"MyLib.Foo",
		DisplayName = "Multi-level folder → dot-separated namespace"
	)]
	[Arguments(
		@"C:\repo\MyProject\",
		@"C:\repo\MyProject\Extensions\TopLevel.cs",
		"",
		DisplayName = "File directly in Extensions → global namespace (empty string)"
	)]
	[Arguments(
		@"C:\repo\MyProject\",
		@"C:\repo\MyProject\Program.cs",
		null,
		DisplayName = "Root file → not in Extensions scope"
	)]
	[Arguments(
		@"C:\repo\MyProject\",
		@"C:\repo\MyProject\Services\Extensions\Foo.cs",
		null,
		DisplayName = "Nested Extensions folder → not in scope"
	)]
	public async Task ComputeExpectedNamespace_ReturnsCorrectResult(
		string projectDir,
		string filePath,
		string? expectedNamespace
	)
	{
		var result = ExtensionsNamespaceHelper.ComputeExpectedNamespace(
			NormalizeFakePath(projectDir),
			NormalizeFakePath(filePath)
		);
		await Assert.That(result).IsEqualTo(expectedNamespace);
	}

	[Test]
	[Arguments(
		@"C:\proj\",
		@"C:\proj\Extensions\A\Foo.cs",
		true,
		DisplayName = "Nested file under root Extensions is in scope"
	)]
	[Arguments(
		@"C:\proj\",
		@"C:\proj\Extensions\Foo.cs",
		true,
		DisplayName = "Top-level file under root Extensions is in scope"
	)]
	[Arguments(@"C:\proj\", @"C:\proj\Program.cs", false, DisplayName = "Project root file is out of scope")]
	[Arguments(
		@"C:\proj\",
		@"C:\proj\Services\Extensions\Foo.cs",
		false,
		DisplayName = "Nested Extensions folder is out of scope"
	)]
	[Arguments(
		@"C:\proj\",
		@"C:\proj\Extensionsfoo\A\Foo.cs",
		false,
		DisplayName = "Partial segment match is out of scope"
	)]
	public async Task IsInExtensionsRootScope_ReturnsCorrectResult(string projectDir, string filePath, bool expected)
	{
		var result = ExtensionsNamespaceHelper.IsInExtensionsRootScope(
			NormalizeFakePath(projectDir),
			NormalizeFakePath(filePath)
		);
		await Assert.That(result).IsEqualTo(expected);
	}

	/// <summary>
	/// Converts a Windows-style fake path literal (e.g. <c>C:\repo\MyProject\</c>) into an
	/// absolute path using the current platform's directory separators. The helper performs
	/// relative-path math with platform path APIs, so tests must feed it native-format paths.
	/// </summary>
	static string NormalizeFakePath(string windowsPath)
	{
		var slashed = windowsPath.Replace('\\', '/');

		if (slashed.Length >= 2 && slashed[1] == ':')
			slashed = slashed[2..];

		return Path.Combine(Path.GetTempPath(), slashed.TrimStart('/'));
	}
}
