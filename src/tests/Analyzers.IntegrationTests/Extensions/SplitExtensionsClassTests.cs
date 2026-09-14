using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Purview.DotNetProjectSdk.CodeFixers.ExtensionsNamespace;
using System.Collections.Immutable;
using System.Composition;

namespace Purview.DotNetProjectSdk.Analyzers.IntegrationTests.Extensions;

/// <summary>
/// Integration tests for <see cref="SplitExtensionsClassCodeRefactoringProvider"/>.
/// </summary>
[Category("Integration")]
public sealed class SplitExtensionsClassTests
{
	const string SplitReceiverSource = """
		namespace Microsoft.Extensions.Hosting
		{
			public interface IHostApplicationBuilder { }
		}

		namespace Microsoft.AspNetCore.Builder
		{
			public class WebApplication { }
		}
		""";

	const string MoveReceiverSource = """
		namespace Microsoft.Extensions.DependencyInjection
		{
			public interface IServiceCollection { }
		}

		namespace Purview.EventSourcing.Admin.API
		{
			public class AdminPortalOptions { }
		}
		""";

	const string SingleReceiverExtensionsSource = """
		namespace Purview.EventSourcing.Admin.API;
		using Microsoft.Extensions.DependencyInjection;
		public static class AdminAPIOpenAPIExtensions
		{
			public static IServiceCollection Add(this IServiceCollection services) => services;
		}
		""";

	[Test]
	public async Task RefactoringProvider_IsExported_ForVisualStudioDiscovery(CancellationToken cancellationToken)
	{
		_ = cancellationToken;

		await Assert.That(typeof(SplitExtensionsClassCodeRefactoringProvider).IsPublic).IsTrue();

		var attributes = Attribute.GetCustomAttributes(
			typeof(SplitExtensionsClassCodeRefactoringProvider),
			inherit: false
		);
		await Assert.That(attributes.OfType<ExportCodeRefactoringProviderAttribute>().Any()).IsTrue();
		await Assert.That(attributes.OfType<SharedAttribute>().Any()).IsTrue();
	}

	[Test]
	public async Task Refactoring_MultiReceiverClass_OffersSplit(CancellationToken cancellationToken)
	{
		const string extensionsSource = """
			namespace Microsoft.Extensions.Hosting;
			using Microsoft.AspNetCore.Builder;
			public static class ServiceDefaultsExtensions
			{
				public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder => builder;
				public static WebApplication MapDefaultEndpoints(this WebApplication app) => app;
			}
			""";

		var (actions, _) = await GetRefactoringsAsync(
			SplitReceiverSource,
			extensionsSource,
			extensionsFolderPath: null,
			cancellationToken
		);

		await Assert.That(actions).Count().IsEqualTo(1);
		await Assert.That(actions.Single().Title).IsEqualTo("Split extensions class into one class per receiver type");
	}

	[Test]
	public async Task Refactoring_Split_ProducesOneClassPerReceiver_UnderExtensionsFolder(
		CancellationToken cancellationToken
	)
	{
		const string extensionsSource = """
			namespace Microsoft.Extensions.Hosting;
			using Microsoft.AspNetCore.Builder;
			public static class ServiceDefaultsExtensions
			{
				public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder => builder;
				public static WebApplication MapDefaultEndpoints(this WebApplication app) => app;
			}
			""";

		var (changedSolution, originalDocumentId) = await ApplySplitAsync(
			extensionsSource,
			cancellationToken: cancellationToken
		);

		// The original class was the only member of its file, so the document is removed.
		await Assert.That(changedSolution.GetDocument(originalDocumentId)).IsNull();

		await AssertExtensionsDocumentAsync(
			changedSolution,
			"HostApplicationBuilderExtensions.cs",
			["Extensions", "Microsoft", "Extensions", "Hosting"],
			"namespace Microsoft.Extensions.Hosting;",
			"public static class HostApplicationBuilderExtensions",
			"AddServiceDefaults",
			doesNotContain: "MapDefaultEndpoints",
			cancellationToken
		);
		await AssertExtensionsDocumentAsync(
			changedSolution,
			"WebApplicationExtensions.cs",
			["Extensions", "Microsoft", "AspNetCore", "Builder"],
			"namespace Microsoft.AspNetCore.Builder;",
			"public static class WebApplicationExtensions",
			"MapDefaultEndpoints",
			doesNotContain: "AddServiceDefaults",
			cancellationToken
		);
	}

	[Test]
	public async Task Refactoring_SingleReceiverClass_OffersMove(CancellationToken cancellationToken)
	{
		var (actions, _) = await GetRefactoringsAsync(
			MoveReceiverSource,
			SingleReceiverExtensionsSource,
			extensionsFolderPath: null,
			cancellationToken
		);

		await Assert.That(actions).Count().IsEqualTo(1);
		await Assert.That(actions.Single().Title).IsEqualTo("Move extensions class to conventional location");
	}

	[Test]
	public async Task Refactoring_ClassWithNonExtensionMembers_OffersMove(CancellationToken cancellationToken)
	{
		const string extensionsSource = """
			namespace Purview.EventSourcing.Admin.API;
			using Microsoft.Extensions.DependencyInjection;
			public static class AdminAPIOpenAPIExtensions
			{
				public const string DocumentName = "admin";
				public static IServiceCollection Add(this IServiceCollection services, AdminPortalOptions options) => services;
				static void Normalize(AdminPortalOptions options) { }
			}
			""";

		var (actions, _) = await GetRefactoringsAsync(
			MoveReceiverSource,
			extensionsSource,
			extensionsFolderPath: null,
			cancellationToken
		);

		await Assert.That(actions).Count().IsEqualTo(1);
		await Assert.That(actions.Single().Title).IsEqualTo("Move extensions class to conventional location");
	}

	[Test]
	public async Task Refactoring_NonStaticClass_DoesNotOfferAnyRefactoring(CancellationToken cancellationToken)
	{
		const string extensionsSource = """
			namespace Purview.EventSourcing.Admin.API;

			public class AdminPortalService
			{
				public void DoWork() { }
			}
			""";

		var (actions, _) = await GetRefactoringsAsync(
			MoveReceiverSource,
			extensionsSource,
			extensionsFolderPath: null,
			cancellationToken
		);

		await Assert.That(actions).IsEmpty();
	}

	[Test]
	public async Task Refactoring_AlreadyConventionalLocation_DoesNotOfferMove(CancellationToken cancellationToken)
	{
		const string extensionsSource = """
			namespace Microsoft.Extensions.DependencyInjection;

			public static class ServiceCollectionExtensions
			{
				public static IServiceCollection Add(this IServiceCollection services) => services;
			}
			""";

		var (actions, _) = await GetRefactoringsAsync(
			MoveReceiverSource,
			extensionsSource,
			extensionsFolderPath: ["Extensions", "Microsoft", "Extensions", "DependencyInjection"],
			cancellationToken
		);

		await Assert.That(actions).IsEmpty();
	}

	[Test]
	public async Task Refactoring_AtConventionalLocation_Misnamed_OffersRename(CancellationToken cancellationToken)
	{
		const string extensionsSource = """
			namespace Microsoft.Extensions.DependencyInjection;

			public static class AdminAPIOpenAPIExtensions
			{
				public static IServiceCollection Add(this IServiceCollection services) => services;
			}
			""";

		var (actions, _) = await GetRefactoringsAsync(
			MoveReceiverSource,
			extensionsSource,
			extensionsFolderPath: ["Extensions", "Microsoft", "Extensions", "DependencyInjection"],
			cancellationToken
		);

		await Assert.That(actions).Count().IsEqualTo(1);
		await Assert.That(actions.Single().Title).IsEqualTo("Rename extensions class to 'ServiceCollectionExtensions'");
	}

	[Test]
	public async Task Refactoring_Move_MergesWithExistingFile_PreservesAllMembers(CancellationToken cancellationToken)
	{
		const string extensionsSource = """
			namespace Purview.EventSourcing.Admin.API;
			using Microsoft.Extensions.DependencyInjection;
			public static class AdminAPIOpenAPIExtensions
			{
				public const string DocumentName = "admin";
				public static IServiceCollection Add(this IServiceCollection services) => services;
				public static IServiceCollection AddAdmin(this IServiceCollection services, AdminPortalOptions options) => services;
			}
			""";

		const string existingExtensionsSource = """
			namespace Microsoft.Extensions.DependencyInjection;
			using Purview.EventSourcing.Admin.API;
			public static class ServiceCollectionExtensions
			{
				public const string ExistingDocumentName = "existing";
				public static IServiceCollection Add(this IServiceCollection services) => services;
				public static IServiceCollection Configure(this IServiceCollection services, AdminPortalOptions options) => services;
			}
			""";

		var (changedSolution, originalDocumentId) = await ApplyMoveAsync(
			extensionsSource,
			consumerSource: null,
			existingExtensionsSource,
			cancellationToken
		);

		// The moved class was the only member of its file, so the document is removed.
		await Assert.That(changedSolution.GetDocument(originalDocumentId)).IsNull();

		var mergedDocument = changedSolution
			.Projects.SelectMany(p => p.Documents)
			.Single(d => d.Name == "ServiceCollectionExtensions.cs");
		await Assert
			.That(mergedDocument.Folders.ToArray())
			.IsEquivalentTo(["Extensions", "Microsoft", "Extensions", "DependencyInjection"]);
		var mergedText = (await mergedDocument.GetTextAsync(cancellationToken)).ToString();

		await Assert.That(mergedText).Contains("namespace Microsoft.Extensions.DependencyInjection;");
		await Assert.That(mergedText).Contains("public static class ServiceCollectionExtensions");
		await Assert
			.That(mergedText)
			.DoesNotContain("public static class AdminAPIOpenAPIExtensions")
			.Because("The moved class must be merged into the existing class.");
		await Assert
			.That(mergedText)
			.Contains("using Purview.EventSourcing.Admin.API;")
			.Because("The merged members may reference types from the moved class's original namespace.");
		await Assert
			.That(mergedText)
			.Contains("public const string DocumentName")
			.Because("The moved class's constant must be preserved.");
		await Assert
			.That(mergedText)
			.Contains("public const string ExistingDocumentName")
			.Because("The existing class's constant must be preserved.");
		await Assert.That(mergedText).Contains("public static IServiceCollection Configure");
		await Assert.That(mergedText).Contains("public static IServiceCollection AddAdmin");

		var duplicateAddCount =
			mergedText.Split("public static IServiceCollection Add(", StringSplitOptions.None).Length - 1;
		await Assert
			.That(duplicateAddCount)
			.IsEqualTo(1)
			.Because("Members with a matching signature in the target class are not duplicated.");
	}

	[Test]
	public async Task Refactoring_Move_Merging_RenamesConsumerReferences(CancellationToken cancellationToken)
	{
		const string extensionsSource = """
			namespace Purview.EventSourcing.Admin.API;
			using Microsoft.Extensions.DependencyInjection;
			public static class AdminAPIOpenAPIExtensions
			{
				public const string DocumentName = "admin";
				public static IServiceCollection Add(this IServiceCollection services) => services;
			}
			""";

		const string existingExtensionsSource = """
			namespace Microsoft.Extensions.DependencyInjection;
			public static class ServiceCollectionExtensions
			{
				public static IServiceCollection Add(this IServiceCollection services) => services;
			}
			""";

		const string consumerSource = """
			namespace Consumer;
			using Purview.EventSourcing.Admin.API;
			public static class Consumer
			{
				public static string Document = AdminAPIOpenAPIExtensions.DocumentName;
			}
			""";

		var (changedSolution, _) = await ApplyMoveAsync(
			extensionsSource,
			consumerSource,
			existingExtensionsSource,
			cancellationToken
		);

		var consumerDocument = changedSolution
			.Projects.SelectMany(p => p.Documents)
			.Single(d => d.Name == "Consumer.cs");
		var consumerText = (await consumerDocument.GetTextAsync(cancellationToken)).ToString();

		await Assert
			.That(consumerText)
			.Contains("ServiceCollectionExtensions.DocumentName")
			.Because($"The merged type must be referenced by consumers.{Environment.NewLine}{consumerText}");
		await Assert
			.That(consumerText)
			.DoesNotContain("AdminAPIOpenAPIExtensions")
			.Because($"Consumers must not reference the removed type name.{Environment.NewLine}{consumerText}");
		await Assert
			.That(consumerText)
			.Contains("using Microsoft.Extensions.DependencyInjection;")
			.Because(
				$"The merged type must be reachable from referencing documents.{Environment.NewLine}{consumerText}"
			);
	}

	[Test]
	public async Task Refactoring_Move_RepathsFileRenamesClass_AndPreservesMembers(CancellationToken cancellationToken)
	{
		const string extensionsSource = """
			namespace Purview.EventSourcing.Admin.API;
			using Microsoft.Extensions.DependencyInjection;
			public static class AdminAPIOpenAPIExtensions
			{
				public const string DocumentName = "admin";
				public static IServiceCollection Add(this IServiceCollection services, AdminPortalOptions options) => services;
				static void Normalize(AdminPortalOptions options) { }
			}
			""";

		var (changedSolution, originalDocumentId) = await ApplyMoveAsync(
			extensionsSource,
			consumerSource: null,
			existingExtensionsSource: null,
			cancellationToken
		);

		// The original class was the only member of its file, so the document is removed.
		await Assert.That(changedSolution.GetDocument(originalDocumentId)).IsNull();

		await AssertExtensionsDocumentAsync(
			changedSolution,
			"ServiceCollectionExtensions.cs",
			["Extensions", "Microsoft", "Extensions", "DependencyInjection"],
			"namespace Microsoft.Extensions.DependencyInjection;",
			"public static class ServiceCollectionExtensions",
			contains: "public const string DocumentName",
			doesNotContain: "namespace Purview.EventSourcing.Admin.API;",
			cancellationToken,
			extraContains: ["static void Normalize", "using Purview.EventSourcing.Admin.API;"]
		);
	}

	[Test]
	public async Task Refactoring_Move_UpdatesReferencesInOtherDocuments(CancellationToken cancellationToken)
	{
		const string extensionsSource = """
			namespace Purview.EventSourcing.Admin.API;
			using Microsoft.Extensions.DependencyInjection;
			public static class AdminAPIOpenAPIExtensions
			{
				public const string DocumentName = "admin";
				public static IServiceCollection Add(this IServiceCollection services) => services;
			}
			""";

		const string consumerSource = """
			namespace Consumer;
			using Purview.EventSourcing.Admin.API;
			public static class Consumer
			{
				public static string Document = AdminAPIOpenAPIExtensions.DocumentName;
			}
			""";

		var (changedSolution, _) = await ApplyMoveAsync(
			extensionsSource,
			consumerSource,
			existingExtensionsSource: null,
			cancellationToken
		);

		var consumerDocument = changedSolution
			.Projects.SelectMany(p => p.Documents)
			.Single(d => d.Name == "Consumer.cs");
		var consumerText = (await consumerDocument.GetTextAsync(cancellationToken)).ToString();

		await Assert
			.That(consumerText)
			.Contains("ServiceCollectionExtensions.DocumentName")
			.Because($"The renamed type must be referenced by consumers.{Environment.NewLine}{consumerText}");
		await Assert
			.That(consumerText)
			.DoesNotContain("AdminAPIOpenAPIExtensions")
			.Because($"Consumers must not reference the removed type name.{Environment.NewLine}{consumerText}");
		await Assert
			.That(consumerText)
			.Contains("using Microsoft.Extensions.DependencyInjection;")
			.Because(
				$"The moved type must be reachable from referencing documents.{Environment.NewLine}{consumerText}"
			);
	}

	[Test]
	public async Task Refactoring_Split_MergesWithExistingFile_ForEachReceiver(CancellationToken cancellationToken)
	{
		const string extensionsSource = """
			namespace Microsoft.Extensions.Hosting;
			using Microsoft.AspNetCore.Builder;
			public static class ServiceDefaultsExtensions
			{
				public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder => builder;
				public static WebApplication MapDefaultEndpoints(this WebApplication app) => app;
			}
			""";

		const string existingHostExtensionsSource = """
			namespace Microsoft.Extensions.Hosting;
			public static class HostApplicationBuilderExtensions
			{
				public static IHostApplicationBuilder AddDefaults(this IHostApplicationBuilder builder) => builder;
			}
			""";

		var (changedSolution, originalDocumentId) = await ApplySplitAsync(
			extensionsSource,
			existingHostExtensionsSource,
			cancellationToken
		);

		// The original class was the only member of its file, so the document is removed.
		await Assert.That(changedSolution.GetDocument(originalDocumentId)).IsNull();

		var mergedDocument = changedSolution
			.Projects.SelectMany(p => p.Documents)
			.Single(d => d.Name == "HostApplicationBuilderExtensions.cs");
		var mergedText = (await mergedDocument.GetTextAsync(cancellationToken)).ToString();
		await Assert
			.That(mergedText)
			.Contains("AddDefaults")
			.Because("The existing receiver class's members must be preserved.");
		await Assert
			.That(mergedText)
			.Contains("AddServiceDefaults")
			.Because("The split receiver's methods must be merged into the existing class.");

		await AssertExtensionsDocumentAsync(
			changedSolution,
			"WebApplicationExtensions.cs",
			["Extensions", "Microsoft", "AspNetCore", "Builder"],
			"namespace Microsoft.AspNetCore.Builder;",
			"public static class WebApplicationExtensions",
			"MapDefaultEndpoints",
			doesNotContain: "AddServiceDefaults",
			cancellationToken
		);
	}

	static async Task<(Solution ChangedSolution, DocumentId OriginalDocumentId)> ApplySplitAsync(
		string extensionsSource,
		string? existingExtensionsSource = null,
		CancellationToken cancellationToken = default
	)
	{
		using var workspace = new AdhocWorkspace();
		var solution = CreateSolution(
			workspace,
			SplitReceiverSource,
			extensionsSource,
			null,
			out var extensionsDocumentId
		);
		if (existingExtensionsSource is not null)
		{
			var existingDocumentId = DocumentId.CreateNewId(solution.Projects.Single().Id);
			solution = solution.AddDocument(
				existingDocumentId,
				"HostApplicationBuilderExtensions.cs",
				SourceText.From(existingExtensionsSource),
				["Extensions", "Microsoft", "Extensions", "Hosting"]
			);
		}

		var document = solution.GetDocument(extensionsDocumentId)!;

		var actions = await ComputeRefactoringsAsync(document, cancellationToken);
		var operations = await actions.Single().GetOperationsAsync(cancellationToken);
		return (((ApplyChangesOperation)operations.Single()).ChangedSolution, extensionsDocumentId);
	}

	static async Task<(Solution ChangedSolution, DocumentId OriginalDocumentId)> ApplyMoveAsync(
		string extensionsSource,
		string? consumerSource,
		string? existingExtensionsSource,
		CancellationToken cancellationToken
	)
	{
		using var workspace = new AdhocWorkspace();
		var solution = CreateSolution(
			workspace,
			MoveReceiverSource,
			extensionsSource,
			null,
			out var extensionsDocumentId
		);
		if (existingExtensionsSource is not null)
		{
			var existingDocumentId = DocumentId.CreateNewId(solution.Projects.Single().Id);
			solution = solution.AddDocument(
				existingDocumentId,
				"ServiceCollectionExtensions.cs",
				SourceText.From(existingExtensionsSource),
				["Extensions", "Microsoft", "Extensions", "DependencyInjection"]
			);
		}
		if (consumerSource is not null)
		{
			var consumerDocumentId = DocumentId.CreateNewId(solution.Projects.Single().Id);
			solution = solution.AddDocument(consumerDocumentId, "Consumer.cs", SourceText.From(consumerSource));
		}

		var document = solution.GetDocument(extensionsDocumentId)!;
		var actions = await ComputeRefactoringsAsync(document, cancellationToken);
		var moveAction = actions.Single(a => a.Title == "Move extensions class to conventional location");
		var operations = await moveAction.GetOperationsAsync(cancellationToken);
		return (((ApplyChangesOperation)operations.Single()).ChangedSolution, extensionsDocumentId);
	}

	static async Task AssertExtensionsDocumentAsync(
		Solution solution,
		string documentName,
		string[] folders,
		string namespaceText,
		string classText,
		string contains,
		string doesNotContain,
		CancellationToken cancellationToken,
		string[]? extraContains = null
	)
	{
		var document = solution.Projects.SelectMany(p => p.Documents).Single(d => d.Name == documentName);
		await Assert
			.That(document.Folders.ToArray())
			.IsEquivalentTo(folders)
			.Because($"Actual folders: {string.Join(", ", document.Folders)}");

		var text = (await document.GetTextAsync(cancellationToken)).ToString();
		await Assert.That(text).Contains(namespaceText);
		await Assert.That(text).Contains(classText);
		await Assert.That(text).Contains(contains);
		await Assert.That(text).DoesNotContain(doesNotContain);

		if (extraContains is not null)
		{
			foreach (var expected in extraContains)
			{
				await Assert.That(text).Contains(expected);
			}
		}
	}

	static async Task<(ImmutableArray<CodeAction> Actions, DocumentId DocumentId)> GetRefactoringsAsync(
		string receiverSource,
		string extensionsSource,
		IEnumerable<string>? extensionsFolderPath,
		CancellationToken cancellationToken
	)
	{
		using var workspace = new AdhocWorkspace();
		var solution = CreateSolution(
			workspace,
			receiverSource,
			extensionsSource,
			extensionsFolderPath,
			out var extensionsDocumentId
		);
		var document = solution.GetDocument(extensionsDocumentId)!;
		var actions = await ComputeRefactoringsAsync(document, cancellationToken);
		return (actions, extensionsDocumentId);
	}

	static async Task<ImmutableArray<CodeAction>> ComputeRefactoringsAsync(
		Document document,
		CancellationToken cancellationToken
	)
	{
		var root = await document.GetSyntaxRootAsync(cancellationToken);
		var classDeclaration = root!.DescendantNodes().OfType<ClassDeclarationSyntax>().First();

		var provider = new SplitExtensionsClassCodeRefactoringProvider();
		var actions = new List<CodeAction>();
		var context = new CodeRefactoringContext(
			document,
			classDeclaration.Identifier.Span,
			actions.Add,
			cancellationToken
		);

		await provider.ComputeRefactoringsAsync(context);
		return [.. actions];
	}

	static Solution CreateSolution(
		AdhocWorkspace workspace,
		string receiverSource,
		string extensionsSource,
		IEnumerable<string>? extensionsFolderPath,
		out DocumentId extensionsDocumentId
	)
	{
		var projectId = ProjectId.CreateNewId();
		var project = ProjectInfo
			.Create(
				projectId,
				VersionStamp.Create(),
				"TestProject",
				"TestProject",
				LanguageNames.CSharp,
				parseOptions: CSharpParseOptions.Default,
				compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
			)
			.WithMetadataReferences(AnalyzerTestInfrastructure.BuildBclReferences());

		var receiverDocumentId = DocumentId.CreateNewId(projectId);
		extensionsDocumentId = DocumentId.CreateNewId(projectId);
		return workspace
			.CurrentSolution.AddProject(project)
			.AddDocument(receiverDocumentId, "Receivers.cs", SourceText.From(receiverSource))
			.AddDocument(
				extensionsDocumentId,
				"Extensions.cs",
				SourceText.From(extensionsSource),
				extensionsFolderPath
			);
	}
}
