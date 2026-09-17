using System.Runtime.InteropServices;

namespace Purview.BuildSdk.Analyzers.ExtensionsNamespace;

/// <summary>
/// Helper methods for deriving and validating namespace conventions for files under the
/// project-root <c>Extensions</c> directory.
/// </summary>
static class ExtensionsNamespaceHelper
{
	const string ExtensionsRootFolderName = "Extensions";

	static readonly StringComparison PathSegmentComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
		? StringComparison.OrdinalIgnoreCase
		: StringComparison.Ordinal;

	static readonly StringComparison FileExtensionComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
		? StringComparison.OrdinalIgnoreCase
		: StringComparison.Ordinal;
	static readonly char[] DirectorySeparators = ['\\', '/'];

	internal static bool IsInExtensionsRootScope(string projectDir, string filePath)
	{
		return ComputeExpectedNamespace(projectDir, filePath) is not null;
	}

	internal static string? ComputeExpectedNamespace(string projectDir, string filePath)
	{
		if (string.IsNullOrWhiteSpace(projectDir) || string.IsNullOrWhiteSpace(filePath))
		{
			return null;
		}

		if (!filePath.EndsWith(".cs", FileExtensionComparison))
		{
			return null;
		}

		string fullProjectDir;
		string fullFilePath;

		try
		{
			fullProjectDir = EnsureTrailingDirectorySeparator(Path.GetFullPath(projectDir));
			fullFilePath = Path.GetFullPath(filePath);
		}
		catch (ArgumentException)
		{
			return null;
		}
		catch (NotSupportedException)
		{
			return null;
		}
		catch (PathTooLongException)
		{
			return null;
		}

		var relativePath = GetRelativePath(fullProjectDir, fullFilePath);
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			return null;
		}

		if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
		{
			return null;
		}

		var relativeSegments = relativePath.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries);
		relativeSegments = [.. relativeSegments.Select(static s => s.Trim()).Where(static s => s.Length > 0)];

		if (relativeSegments.Length < 2)
		{
			return null;
		}

		if (!string.Equals(relativeSegments[0], ExtensionsRootFolderName, PathSegmentComparison))
		{
			return null;
		}

		var folderSegmentsCount = relativeSegments.Length - 2;
		if (folderSegmentsCount <= 0)
		{
			return string.Empty;
		}

		// The expected namespace is derived from the segments of the relative path that are between the "Extensions" folder and the file name.
		return string.Join(".", relativeSegments.Skip(1).Take(folderSegmentsCount));
	}

	static string GetRelativePath(string basePath, string fullPath)
	{
		var baseUri = new Uri(EnsureTrailingDirectorySeparator(basePath), UriKind.Absolute);
		var fullUri = new Uri(fullPath, UriKind.Absolute);
		var relativeUri = baseUri.MakeRelativeUri(fullUri);
		return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
	}

	static string EnsureTrailingDirectorySeparator(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return path;
		}

		var lastChar = path[path.Length - 1];
		if (lastChar == Path.DirectorySeparatorChar || lastChar == Path.AltDirectorySeparatorChar)
		{
			return path;
		}

		// Append the platform-specific directory separator character to the end of the path.
		return path + Path.DirectorySeparatorChar;
	}
}
