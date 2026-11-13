namespace Purview.BuildSdk.PropertySchema;

/// <summary>
/// The deliberate exceptions to "every public property the SDK defines has a schema entry". Each one
/// carries its reason, and adding a name here is the only way to leave a property out of the schema.
/// </summary>
static class SdkPropertyClassification
{
	/// <summary>
	/// Properties owned by MSBuild, the .NET SDK, NuGet or a test platform that this SDK defaults or
	/// reads. MSBuild Editor already ships schemas for them, and how a sidecar entry merges with a
	/// built-in one is unverified - so the sidecar does not redescribe them.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> ExternallyOwned = new Dictionary<string, string>(
		StringComparer.Ordinal
	)
	{
		["AccelerateBuildsInVisualStudio"] = "Visual Studio",
		["AllowedOutputExtensionsInPackageBuildOutputFolder"] = "NuGet pack",
		["AnalysisLevel"] = ".NET SDK",
		["AnalysisMode"] = ".NET SDK",
		["AssemblyName"] = "MSBuild",
		["BeforeMicrosoftNETSdkTargets"] = ".NET SDK import hook",
		["CentralPackageTransitivePinningEnabled"] = "NuGet",
		["CollectCoverage"] = "coverlet",
		["CompilerGeneratedFilesOutputPath"] = "Roslyn",
		["ContinuousIntegrationBuild"] = "MSBuild",
		["DebugType"] = "MSBuild",
		["Deterministic"] = "Roslyn",
		["DockerDefaultTargetOS"] = "Visual Studio container tools",
		["DockerfileContext"] = "Visual Studio container tools",
		["EmbedUntrackedSources"] = "SourceLink",
		["EnableDynamicNativeInstrumentation"] = "Microsoft.Testing.Platform",
		["EnableMicrosoftTestingPlatform"] = "Microsoft.Testing.Platform",
		["EnableNETAnalyzers"] = ".NET SDK",
		["EnableStaticNativeInstrumentation"] = "Microsoft.Testing.Platform",
		["EnforceCodeStyleInBuild"] = ".NET SDK",
		["EnforceExtendedAnalyzerRules"] = "Roslyn",
		["Exclude"] = "coverlet",
		["ExcludeByAttribute"] = "coverlet",
		["Features"] = "Roslyn",
		["GenerateAssemblyCompanyAttribute"] = ".NET SDK",
		["GenerateAssemblyCopyrightAttribute"] = ".NET SDK",
		["GenerateDependencyFile"] = ".NET SDK",
		["GenerateDocumentationFile"] = "MSBuild",
		["ImplicitUsings"] = ".NET SDK",
		["IncludeBuildOutput"] = "NuGet pack",
		["IncludeSource"] = "NuGet pack",
		["IncludeSymbols"] = "NuGet pack",
		["InterceptorsNamespaces"] = "Roslyn",
		["InvariantGlobalization"] = ".NET runtime",
		["IsAotCompatible"] = ".NET SDK",
		["IsPackable"] = "NuGet pack",
		["IsPublishable"] = ".NET SDK",
		["IsRoslynComponent"] = "Roslyn tooling",
		["IsTestingPlatformApplication"] = "Microsoft.Testing.Platform",
		["IsTestProject"] = ".NET SDK",
		["LangVersion"] = "Roslyn",
		["ManagePackageVersionsCentrally"] = "NuGet",
		["MaxCpuCount"] = "MSBuild",
		["MSBuildWarningsAsMessages"] = "MSBuild",
		["NoDefaultExcludes"] = "NuGet pack",
		["NoWarn"] = "Roslyn",
		["Nullable"] = "Roslyn",
		["OutputType"] = "MSBuild",
		["PackageId"] = "NuGet pack",
		["PackageReadmeFile"] = "NuGet pack",
		["PackageTags"] = "NuGet pack",
		["PackageVersion"] = "NuGet pack",
		["ProjectDir"] = "MSBuild",
		["PublishAot"] = ".NET SDK",
		["PublishRepositoryUrl"] = "SourceLink",
		["RootNamespace"] = "MSBuild",
		["SymbolPackageFormat"] = "NuGet pack",
		["TargetFramework"] = "MSBuild",
		["TargetFrameworks"] = "MSBuild",
		["TargetsForTfmSpecificContentInPackage"] = "NuGet pack",
		["TestingPlatformDotnetTestSupport"] = "Microsoft.Testing.Platform",
		["TreatWarningsAsErrors"] = "Roslyn",
		["UseMicrosoftTestingPlatformRunner"] = "Microsoft.Testing.Platform",
		["Version"] = "MSBuild",
		["WarningsAsErrors"] = "Roslyn",
		["WarnOnPackingNonPackableProject"] = "NuGet pack",
	};

	/// <summary>
	/// SDK properties that predate the <c>_</c> prefix convention but are implementation detail: the
	/// SDK computes them, and setting one is not supported. Renaming them would break anyone who reads
	/// them, so they are excluded here instead of being offered in IntelliSense.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> Internal = new Dictionary<string, string>(
		StringComparer.Ordinal
	)
	{
		["DefaultTargetFramework"] = "derived from TargetFramework(s) in Sdk.targets",
		["Packable"] = "legacy placeholder in Defaults.props; nothing reads it",
		["PurviewEditorConfigVersionMarker"] = "the marker the SDK writes; changing it orphans every managed file",
		["PurviewLogicalProjectName"] = "computed during project-name parsing",
		["PurviewNamespacePrefix"] = "computed copy of NamespacePrefix",
		["PurviewPolicyExemptNoWarn"] = "records the SDK's own policy NoWarn entries for PRSGD0008",
		["PurviewProjectShortName"] = "computed during project-name parsing",
		["PurviewRepositoryRspVersionMarker"] = "the marker the SDK writes; changing it orphans every managed file",
		["PurviewTestType"] = "computed during test-type detection",
	};

	/// <summary>
	/// Names referenced by the SDK but neither defined nor offered: aliases kept for compatibility.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(
		StringComparer.Ordinal
	)
	{
		["UseRuntimeAsync"] = "alias for PurviewRuntimeAsync, documented there",
	};

	/// <summary>
	/// Names a property table lists that are not SDK properties, so they have no schema entry.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> DocumentedNonProperties = new Dictionary<string, string>(
		StringComparer.Ordinal
	)
	{
		["AllowUnsafeBlocks"] = "the .NET 11 table states the memory-safety tiers never change it",
		["NamespaceRemoveSuffix"] = "an item type, listed beside the properties that shape RootNamespace",
	};

	/// <summary>
	/// Schema entries whose value in the reference project depends on the machine, the date or the
	/// project, so the schema states no <c>defaultValue</c>. Every other entry without one must evaluate
	/// to empty.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> ComputedDefaults = new Dictionary<string, string>(
		StringComparer.Ordinal
	)
	{
		["CurrentYear"] = "the current year",
		["EditorConfigFilePath"] = "the SDK's install location",
		["NamespacePrefix"] = "required; the reference project has to set it",
		["PurviewBuildSdkVersionForGlobalJson"] = "the restored or packed SDK version",
		["VersionDetectionCacheFile"] = "derived from the repository path",
		// Detection results: the value describes the project, so a "default" would only describe the
		// reference project.
		["IsAspireHostProject"] = "detected",
		["IsBffProject"] = "detected",
		["IsBlazorHostProject"] = "detected",
		["IsBlazorWebAssemblyProject"] = "detected",
		["IsClientSideProject"] = "detected",
		["IsCLIProject"] = "detected",
		["IsContainerProject"] = "detected",
		["IsCSharpProject"] = "detected",
		["IsRazorClassLibraryProject"] = "detected",
		["IsSdkProject"] = "detected",
		["IsServerSharedProject"] = "detected",
		["IsSharedProject"] = "detected",
		["IsSharedTestingProject"] = "detected",
		["IsWebProject"] = "detected",
		["IsWebSdkProject"] = "detected",
		["IsWorkerSdkProject"] = "detected",
		["SdkProjectName"] = "detected",
	};

	/// <summary>
	/// Schema entries the throwaway harness sets itself, so the reference project cannot observe the
	/// shipped default for them.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> SetByTheHarness = new Dictionary<string, string>(
		StringComparer.Ordinal
	)
	{
		["BootstrapMultiThreadedBuildRspToRepoRoot"] =
			"ProjectHarness writes false so tests never change the build host",
	};
}
