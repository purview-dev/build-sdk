using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace Purview.BuildSdk.CodeFixers.ExtensionsNamespace;

/// <summary>
/// Offers to reorganise static extensions classes that do not follow the PDS0002 Extensions-namespace
/// convention. A class targeting multiple receiver types can be split into one class per receiver; a
/// class targeting a single receiver can be moved to its conventional location
/// (<c>Extensions/&lt;receiver namespace&gt;/&lt;Type&gt;Extensions.cs</c>) with its namespace fixed,
/// renamed to the receiver-derived name, merged into an existing conventional file when one already
/// exists, and references in other documents updated.
/// </summary>
[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(SplitExtensionsClassCodeRefactoringProvider))]
[Shared]
public sealed class SplitExtensionsClassCodeRefactoringProvider : CodeRefactoringProvider
{
	internal const string SplitExtensionsClassEquivalenceKey = "SplitExtensionsClass";
	internal const string MoveExtensionsClassEquivalenceKey = "MoveExtensionsClass";
	internal const string RenameExtensionsClassEquivalenceKey = "RenameExtensionsClass";

	public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
		if (root is null)
		{
			return;
		}

		var classDeclaration = root.FindNode(context.Span).FirstAncestorOrSelf<ClassDeclarationSyntax>();
		if (classDeclaration is null)
		{
			return;
		}

		var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken);
		if (semanticModel is null)
		{
			return;
		}

		var typeSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
		if (typeSymbol is null || !typeSymbol.IsStatic)
		{
			return;
		}

		var groups = ComputeReceiverGroups(typeSymbol);
		if (groups.Length == 0)
		{
			return;
		}

		if (groups.Length >= 2)
		{
			// Only offer the split when every declared member is an extension method, so nothing is dropped.
			foreach (var member in typeSymbol.GetMembers())
			{
				if (member is not IMethodSymbol { IsExtensionMethod: true })
				{
					return;
				}
			}

			context.RegisterRefactoring(
				CodeAction.Create(
					"Split extensions class into one class per receiver type",
					cancellationToken =>
						SplitAsync(context.Document, typeSymbol, classDeclaration, groups, cancellationToken),
					equivalenceKey: SplitExtensionsClassEquivalenceKey
				)
			);
			return;
		}

		var receiver = groups[0].Receiver;
		var receiverNamespace = receiver.ContainingNamespace.ToDisplayString();
		var currentNamespace = typeSymbol.ContainingNamespace.ToDisplayString();
		var newTypeName = StripInterfacePrefix(receiver.Name) + "Extensions";
		var atConventionalLocation = IsConventionalLocation(
			context.Document.Folders,
			currentNamespace,
			receiverNamespace
		);
		if (atConventionalLocation && string.Equals(typeSymbol.Name, newTypeName, StringComparison.Ordinal))
		{
			return;
		}

		var title = atConventionalLocation
			? $"Rename extensions class to '{newTypeName}'"
			: "Move extensions class to conventional location";
		var equivalenceKey = atConventionalLocation
			? RenameExtensionsClassEquivalenceKey
			: MoveExtensionsClassEquivalenceKey;

		context.RegisterRefactoring(
			CodeAction.Create(
				title,
				cancellationToken =>
					MoveAsync(context.Document, typeSymbol, classDeclaration, receiver, cancellationToken),
				equivalenceKey: equivalenceKey
			)
		);
	}

	static bool IsConventionalLocation(IReadOnlyList<string> folders, string currentNamespace, string receiverNamespace)
	{
		return string.Equals(currentNamespace, receiverNamespace, StringComparison.Ordinal)
			&& folders.SequenceEqual(ComputeConventionalFolders(receiverNamespace));
	}

	static ImmutableArray<(INamedTypeSymbol Receiver, ImmutableArray<IMethodSymbol> Methods)> ComputeReceiverGroups(
		INamedTypeSymbol type
	)
	{
		var groups = new List<(INamedTypeSymbol Receiver, List<IMethodSymbol> Methods)>();
		var index = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);

		foreach (var member in type.GetMembers())
		{
			if (member is not IMethodSymbol { IsExtensionMethod: true } method || method.Parameters.Length == 0)
			{
				continue;
			}

			var receiver = GetReceiverType(method.Parameters[0].Type);
			if (receiver is null)
			{
				return [];
			}

			if (!index.TryGetValue(receiver, out var groupIndex))
			{
				index[receiver] = groups.Count;
				groups.Add((receiver, new List<IMethodSymbol>()));
				groupIndex = groups.Count - 1;
			}

			groups[groupIndex].Methods.Add(method);
		}

		return [.. groups.Select(group => (group.Receiver, group.Methods.ToImmutableArray()))];
	}

	static INamedTypeSymbol? GetReceiverType(ITypeSymbol type)
	{
		if (type is ITypeParameterSymbol typeParameter)
		{
			foreach (var constraint in typeParameter.ConstraintTypes)
			{
				if (constraint is INamedTypeSymbol named)
				{
					return named;
				}
			}

			return null;
		}

		return type as INamedTypeSymbol;
	}

	static async Task<Solution> SplitAsync(
		Document document,
		INamedTypeSymbol typeSymbol,
		ClassDeclarationSyntax originalClass,
		ImmutableArray<(INamedTypeSymbol Receiver, ImmutableArray<IMethodSymbol> Methods)> groups,
		CancellationToken cancellationToken
	)
	{
		var solution = document.Project.Solution;
		var root = await document.GetSyntaxRootAsync(cancellationToken);
		if (root is not CompilationUnitSyntax originalCompilationUnit)
		{
			return solution;
		}

		var originalNamespace = typeSymbol.ContainingNamespace.ToDisplayString();
		var compilation = await document.Project.GetCompilationAsync(cancellationToken);
		var newSolution = solution;
		foreach (var (receiver, methods) in groups)
		{
			var @namespace = receiver.ContainingNamespace.ToDisplayString();
			var typeName = StripInterfacePrefix(receiver.Name) + "Extensions";
			var folderPath = ComputeConventionalFolders(@namespace);
			var methodDeclarations = methods.Select(GetMethodSyntax).ToImmutableArray();

			var existingType = compilation?.GetTypeByMetadataName(
				@namespace.Length == 0 ? typeName : @namespace + "." + typeName
			);
			if (existingType is not null)
			{
				var existingSyntax = existingType.DeclaringSyntaxReferences.FirstOrDefault() is { } reference
					? await reference.GetSyntaxAsync(cancellationToken)
					: null;
				if (existingSyntax is not ClassDeclarationSyntax existingClass)
				{
					continue;
				}

				var existingDocumentId = newSolution.GetDocumentId(existingClass.SyntaxTree);
				if (existingDocumentId is null)
				{
					continue;
				}

				var existingDocument = newSolution.GetDocument(existingDocumentId);
				if (existingDocument is null)
				{
					continue;
				}

				var existingRoot = await existingDocument.GetSyntaxRootAsync(cancellationToken);
				if (existingRoot is null)
				{
					continue;
				}

				var incoming = SyntaxFactory
					.ClassDeclaration(typeName)
					.WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(methodDeclarations));
				var updatedRoot = existingRoot.ReplaceNode(existingClass, MergeMembers(existingClass, incoming));
				newSolution = newSolution.WithDocumentSyntaxRoot(
					existingDocumentId,
					updatedRoot,
					PreservationMode.PreserveIdentity
				);

				if (!string.Equals(originalNamespace, @namespace, StringComparison.Ordinal))
				{
					newSolution = await AddNamespaceUsingAsync(
						newSolution,
						newSolution.GetDocument(existingDocumentId)!,
						originalNamespace,
						cancellationToken
					);
				}

				continue;
			}

			var classDeclaration = SyntaxFactory
				.ClassDeclaration(typeName)
				.AddModifiers(
					SyntaxFactory.Token(SyntaxKind.PublicKeyword),
					SyntaxFactory.Token(SyntaxKind.StaticKeyword)
				)
				.WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(methodDeclarations));

			var newCompilationUnit = BuildExtensionsDocument(
				originalCompilationUnit,
				originalNamespace,
				@namespace,
				classDeclaration
			);

			newSolution = newSolution.AddDocument(
				DocumentId.CreateNewId(document.Project.Id),
				typeName + ".cs",
				SourceText.From(newCompilationUnit.ToFullString()),
				folderPath
			);
		}

		return RemoveOriginalClassOrDocument(newSolution, document.Id, originalCompilationUnit, originalClass);
	}

	static async Task<Solution> MoveAsync(
		Document document,
		INamedTypeSymbol typeSymbol,
		ClassDeclarationSyntax originalClass,
		INamedTypeSymbol receiver,
		CancellationToken cancellationToken
	)
	{
		var solution = document.Project.Solution;
		var root = await document.GetSyntaxRootAsync(cancellationToken);
		if (root is not CompilationUnitSyntax originalCompilationUnit)
		{
			return solution;
		}

		var originalNamespace = typeSymbol.ContainingNamespace.ToDisplayString();
		var @namespace = receiver.ContainingNamespace.ToDisplayString();
		var newTypeName = StripInterfacePrefix(receiver.Name) + "Extensions";
		var folderPath = ComputeConventionalFolders(@namespace);

		var referencingDocumentIds = await FindReferencingDocumentIdsAsync(
			solution,
			typeSymbol,
			document.Id,
			cancellationToken
		);

		var compilation = await document.Project.GetCompilationAsync(cancellationToken);
		var existingType = compilation?.GetTypeByMetadataName(
			@namespace.Length == 0 ? newTypeName : @namespace + "." + newTypeName
		);

		if (existingType is null)
		{
			return await MoveToNewFileAsync(
				solution,
				document,
				typeSymbol,
				originalNamespace,
				@namespace,
				newTypeName,
				folderPath,
				referencingDocumentIds,
				cancellationToken
			);
		}

		// Merge the original class into the existing class, and update every reference to the old type name.
		return await MoveMergeIntoExistingAsync(
			solution,
			document,
			typeSymbol,
			originalClass,
			originalCompilationUnit,
			existingType,
			originalNamespace,
			@namespace,
			newTypeName,
			referencingDocumentIds,
			cancellationToken
		);
	}

	static async Task<Solution> MoveToNewFileAsync(
		Solution solution,
		Document document,
		INamedTypeSymbol typeSymbol,
		string originalNamespace,
		string @namespace,
		string newTypeName,
		string[] folderPath,
		ImmutableArray<DocumentId> referencingDocumentIds,
		CancellationToken cancellationToken
	)
	{
		// Rename the type (and every reference) before building the new document, so the moved file
		// carries the conventional name and consumers are updated in one step.
		var renamedSolution = await Renamer.RenameSymbolAsync(
			solution,
			typeSymbol,
			new SymbolRenameOptions(),
			newTypeName,
			cancellationToken
		);

		var renamedDocument = renamedSolution.GetDocument(document.Id);
		if (renamedDocument is null)
		{
			return solution;
		}

		var renamedRoot = await renamedDocument.GetSyntaxRootAsync(cancellationToken);
		if (renamedRoot is not CompilationUnitSyntax renamedCompilationUnit)
		{
			return solution;
		}

		var renamedClass = renamedCompilationUnit
			.DescendantNodes()
			.OfType<ClassDeclarationSyntax>()
			.First(declaration => declaration.Identifier.Text == newTypeName);

		var newCompilationUnit = BuildExtensionsDocument(
			renamedCompilationUnit,
			originalNamespace,
			@namespace,
			renamedClass
		);

		var newSolution = renamedSolution.AddDocument(
			DocumentId.CreateNewId(document.Project.Id),
			newTypeName + ".cs",
			SourceText.From(newCompilationUnit.ToFullString()),
			folderPath
		);
		newSolution = RemoveOriginalClassOrDocument(newSolution, document.Id, renamedCompilationUnit, renamedClass);

		// Add the missing using for the moved type's new namespace to every referencing document.
		foreach (var documentId in referencingDocumentIds)
		{
			var referencingDocument = newSolution.GetDocument(documentId);
			if (referencingDocument is null)
			{
				continue;
			}

			newSolution = await AddNamespaceUsingAsync(newSolution, referencingDocument, @namespace, cancellationToken);
		}

		return newSolution;
	}

	static async Task<Solution> MoveMergeIntoExistingAsync(
		Solution solution,
		Document document,
		INamedTypeSymbol typeSymbol,
		ClassDeclarationSyntax originalClass,
		CompilationUnitSyntax originalCompilationUnit,
		INamedTypeSymbol existingType,
		string originalNamespace,
		string @namespace,
		string newTypeName,
		ImmutableArray<DocumentId> referencingDocumentIds,
		CancellationToken cancellationToken
	)
	{
		if (existingType.DeclaringSyntaxReferences.FirstOrDefault() is not { } reference)
		{
			return solution;
		}

		var existingSyntax = await reference.GetSyntaxAsync(cancellationToken);
		if (existingSyntax is not ClassDeclarationSyntax existingClass)
		{
			return solution;
		}

		var existingDocumentId = solution.GetDocumentId(existingClass.SyntaxTree);
		if (existingDocumentId is null)
		{
			return solution;
		}

		var existingDocument = solution.GetDocument(existingDocumentId);
		if (existingDocument is null)
		{
			return solution;
		}

		var existingRoot = await existingDocument.GetSyntaxRootAsync(cancellationToken);
		if (existingRoot is null)
		{
			return solution;
		}

		// Determine every document that references the old type name up-front, against the original
		// solution where the type still exists, so the identifiers can be rewritten after the merge.
		var references = await SymbolFinder.FindReferencesAsync(typeSymbol, solution, cancellationToken);
		var affectedDocumentIds = references
			.SelectMany(reference => reference.Locations)
			.Where(location => location.Document is not null)
			.Select(location => location.Document!.Id)
			.Append(existingDocumentId)
			.Distinct()
			.ToImmutableArray();

		var updatedRoot = existingRoot.ReplaceNode(existingClass, MergeMembers(existingClass, originalClass));
		var newSolution = solution.WithDocumentSyntaxRoot(
			existingDocumentId,
			updatedRoot,
			PreservationMode.PreserveIdentity
		);

		// The merged members may reference types from the class's original namespace.
		if (!string.Equals(originalNamespace, @namespace, StringComparison.Ordinal))
		{
			newSolution = await AddNamespaceUsingAsync(
				newSolution,
				newSolution.GetDocument(existingDocumentId)!,
				originalNamespace,
				cancellationToken
			);
		}

		// The original class is consumed by the merge (or its whole document removed when nothing remains).
		newSolution = RemoveOriginalClassOrDocument(newSolution, document.Id, originalCompilationUnit, originalClass);

		// Rewrite references to the old type name in every affected document.
		newSolution = await RenameTypeReferencesAsync(
			newSolution,
			affectedDocumentIds,
			typeSymbol.Name,
			newTypeName,
			cancellationToken
		);

		foreach (var documentId in referencingDocumentIds)
		{
			var referencingDocument = newSolution.GetDocument(documentId);
			if (referencingDocument is null)
			{
				continue;
			}

			newSolution = await AddNamespaceUsingAsync(newSolution, referencingDocument, @namespace, cancellationToken);
		}

		return newSolution;
	}

	static async Task<Solution> RenameTypeReferencesAsync(
		Solution solution,
		ImmutableArray<DocumentId> affectedDocumentIds,
		string oldName,
		string newName,
		CancellationToken cancellationToken
	)
	{
		var newSolution = solution;
		foreach (var documentId in affectedDocumentIds)
		{
			var target = newSolution.GetDocument(documentId);
			if (target is null)
			{
				continue;
			}

			var root = await target.GetSyntaxRootAsync(cancellationToken);
			if (root is null)
			{
				continue;
			}

			var renamedRoot = root.ReplaceNodes(
				root.DescendantNodes().OfType<SimpleNameSyntax>(),
				(node, _) =>
					node.Identifier.Text == oldName ? node.WithIdentifier(SyntaxFactory.Identifier(newName)) : node
			);
			if (!renamedRoot.IsEquivalentTo(root, topLevel: false))
			{
				newSolution = newSolution.WithDocumentSyntaxRoot(
					documentId,
					renamedRoot,
					PreservationMode.PreserveIdentity
				);
			}
		}

		return newSolution;
	}

	static ClassDeclarationSyntax MergeMembers(ClassDeclarationSyntax target, ClassDeclarationSyntax incoming)
	{
		var existingKeys = new HashSet<string>(target.Members.Select(MemberKey), StringComparer.Ordinal);
		var mergedMembers = new List<MemberDeclarationSyntax>(target.Members);
		foreach (var member in incoming.Members)
		{
			if (existingKeys.Add(MemberKey(member)))
			{
				mergedMembers.Add(member);
			}
		}

		var mergedClass = target.WithMembers(SyntaxFactory.List(mergedMembers));
		if (incoming.AttributeLists.Count > 0)
		{
			mergedClass = mergedClass.WithAttributeLists(mergedClass.AttributeLists.AddRange(incoming.AttributeLists));
		}

		if (incoming.BaseList is not null)
		{
			mergedClass = mergedClass.WithBaseList(
				mergedClass.BaseList is null
					? incoming.BaseList
					: SyntaxFactory.BaseList(mergedClass.BaseList.Types.AddRange(incoming.BaseList.Types))
			);
		}

		return mergedClass;
	}

	static string MemberKey(MemberDeclarationSyntax member)
	{
		var name = member switch
		{
			MethodDeclarationSyntax method => method.Identifier.Text,
			PropertyDeclarationSyntax property => property.Identifier.Text,
			EventDeclarationSyntax @event => @event.Identifier.Text,
			ConstructorDeclarationSyntax constructor => constructor.Identifier.Text,
			FieldDeclarationSyntax field => string.Join(
				",",
				field.Declaration.Variables.Select(variable => variable.Identifier.Text)
			),
			_ => member.ToString(),
		};

		if (member is MethodDeclarationSyntax methodDeclaration)
		{
			var parameters = string.Join(
				",",
				methodDeclaration.ParameterList.Parameters.Select(parameter => parameter.Type?.ToString())
			);
			return $"{name}({parameters})";
		}

		return name;
	}

	static async Task<Solution> AddNamespaceUsingAsync(
		Solution solution,
		Document document,
		string @namespace,
		CancellationToken cancellationToken
	)
	{
		var root = await document.GetSyntaxRootAsync(cancellationToken);
		if (root is not CompilationUnitSyntax compilationUnit)
		{
			return solution;
		}

		// Skip when the namespace is already imported or is the file's own namespace.
		var existingUsings = compilationUnit.Usings.Select(usingDirective => usingDirective.Name?.ToString());
		var declaredNamespace = compilationUnit
			.DescendantNodes()
			.OfType<BaseNamespaceDeclarationSyntax>()
			.FirstOrDefault()
			?.Name.ToString();
		if (
			@namespace.Length == 0
			|| existingUsings.Contains(@namespace)
			|| string.Equals(declaredNamespace, @namespace, StringComparison.Ordinal)
		)
		{
			return solution;
		}

		var updatedRoot = compilationUnit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(@namespace)));
		return solution.WithDocumentSyntaxRoot(document.Id, updatedRoot, PreservationMode.PreserveIdentity);
	}

	static async Task<ImmutableArray<DocumentId>> FindReferencingDocumentIdsAsync(
		Solution solution,
		ISymbol symbol,
		DocumentId sourceDocumentId,
		CancellationToken cancellationToken
	)
	{
		var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);
		var ids = new List<DocumentId>();
		foreach (var reference in references)
		{
			foreach (var location in reference.Locations)
			{
				if (location.Document is not null && location.Document.Id != sourceDocumentId)
				{
					ids.Add(location.Document.Id);
				}
			}
		}

		return [.. ids.Distinct()];
	}

	static CompilationUnitSyntax BuildExtensionsDocument(
		CompilationUnitSyntax originalCompilationUnit,
		string originalNamespace,
		string newNamespace,
		ClassDeclarationSyntax classDeclaration
	)
	{
		var usings = originalCompilationUnit.Usings;
		if (originalNamespace.Length > 0 && !string.Equals(originalNamespace, newNamespace, StringComparison.Ordinal))
		{
			usings = usings.Add(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(originalNamespace)));
		}

		var newCompilationUnit =
			newNamespace.Length == 0
				? SyntaxFactory.CompilationUnit().WithUsings(usings).AddMembers(classDeclaration)
				: SyntaxFactory
					.CompilationUnit()
					.WithUsings(usings)
					.AddMembers(
						SyntaxFactory
							.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(newNamespace))
							.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(classDeclaration))
					);

		return newCompilationUnit.NormalizeWhitespace();
	}

	static string[] ComputeConventionalFolders(string @namespace) =>
		@namespace.Length == 0 ? ["Extensions"] : ["Extensions", .. @namespace.Split('.')];

	static Solution RemoveOriginalClassOrDocument(
		Solution solution,
		DocumentId documentId,
		CompilationUnitSyntax originalCompilationUnit,
		ClassDeclarationSyntax originalClass
	)
	{
		var updatedRoot = originalCompilationUnit.RemoveNode(originalClass, SyntaxRemoveOptions.KeepExteriorTrivia);
		if (updatedRoot is null)
			return solution;

		// If nothing but usings/empty namespaces remains, the original file is gone.
		var hasRemainingTypes = updatedRoot
			.DescendantNodes()
			.Any(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);

		if (!hasRemainingTypes)
			return solution.RemoveDocument(documentId);

		// Otherwise, keep the original file but remove the class declaration.
		return solution.WithDocumentSyntaxRoot(documentId, updatedRoot, PreservationMode.PreserveIdentity);
	}

	static MethodDeclarationSyntax GetMethodSyntax(IMethodSymbol method)
	{
		var reference = method.DeclaringSyntaxReferences.FirstOrDefault();
		if (reference?.GetSyntax() is MethodDeclarationSyntax syntax)
			return syntax;

		// If the method is declared in source but we can't get its syntax, something is wrong.
		throw new InvalidOperationException($"Unable to resolve the declaration of '{method.Name}'.");
	}

	static string StripInterfacePrefix(string name) =>
		name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) ? name.Substring(1) : name;
}
