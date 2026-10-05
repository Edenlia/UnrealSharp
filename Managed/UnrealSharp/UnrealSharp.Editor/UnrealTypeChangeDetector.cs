using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnrealSharp.Editor;

[Flags]
// ReSharper disable once InconsistentNaming
public enum ECSTypeStructuralFlags : byte
{
    None = 0,
    StructuralChanges = 1 << 0,
    ConstructorChanges = 1 << 1,
}

internal readonly record struct UnrealTypeChange(
    BaseTypeDeclarationSyntax Declaration, ECSTypeStructuralFlags Flags);

internal static class UnrealTypeChangeDetector
{
    // Both compilations must be the current incremental snapshots, not the
    // Project's initial compilation. A declaration can acquire its UClass/UStruct
    // attribute from another partial declaration in an unchanged syntax tree.
    internal static IReadOnlyList<UnrealTypeChange> GetChanges(
        Compilation oldCompilation, SyntaxTree? oldTree,
        Compilation newCompilation, SyntaxTree? newTree)
    {
        Dictionary<string, List<BaseTypeDeclarationSyntax>> oldTypes = GetUnrealTypes(oldCompilation, oldTree);
        Dictionary<string, List<BaseTypeDeclarationSyntax>> newTypes = GetUnrealTypes(newCompilation, newTree);
        HashSet<string> names = new HashSet<string>(oldTypes.Keys, StringComparer.Ordinal);
        names.UnionWith(newTypes.Keys);

        List<UnrealTypeChange> changes = new List<UnrealTypeChange>();
        foreach (string name in names)
        {
            oldTypes.TryGetValue(name, out List<BaseTypeDeclarationSyntax>? oldParts);
            newTypes.TryGetValue(name, out List<BaseTypeDeclarationSyntax>? newParts);
            oldParts ??= [];
            newParts ??= [];

            if (AreEquivalent(oldParts, newParts))
            {
                continue;
            }

            ECSTypeStructuralFlags flags = ECSTypeStructuralFlags.StructuralChanges;
            if (!AreEquivalent(GetConstructors(oldParts), GetConstructors(newParts)))
            {
                flags |= ECSTypeStructuralFlags.ConstructorChanges;
            }

            // Include removed declarations/files: the surviving partial type
            // must lose their reflected members during reload as well.
            changes.Add(new UnrealTypeChange(newParts.Count > 0 ? newParts[0] : oldParts[0], flags));
        }

        return changes;
    }

    private static Dictionary<string, List<BaseTypeDeclarationSyntax>> GetUnrealTypes(
        Compilation compilation, SyntaxTree? tree)
    {
        Dictionary<string, List<BaseTypeDeclarationSyntax>> types = new Dictionary<string, List<BaseTypeDeclarationSyntax>>(StringComparer.Ordinal);
        if (tree == null)
        {
            return types;
        }

        SemanticModel model = compilation.GetSemanticModel(tree);
        foreach (BaseTypeDeclarationSyntax declaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol || !IsUnrealType(symbol))
            {
                continue;
            }

            // Simple identifiers can collide across namespaces, and one tree
            // can contain several declarations of the same partial type.
            string name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!types.TryGetValue(name, out List<BaseTypeDeclarationSyntax>? declarations))
            {
                declarations = new List<BaseTypeDeclarationSyntax>();
                types.Add(name, declarations);
            }

            declarations.Add(declaration);
        }

        return types;
    }

    private static bool IsUnrealType(INamedTypeSymbol symbol)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() is
                "UnrealSharp.Attributes.UClassAttribute" or
                "UnrealSharp.Attributes.UStructAttribute" or
                "UnrealSharp.Attributes.UEnumAttribute" or
                "UnrealSharp.Attributes.UInterfaceAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static List<ConstructorDeclarationSyntax> GetConstructors(IEnumerable<BaseTypeDeclarationSyntax> parts)
    {
        return parts.OfType<TypeDeclarationSyntax>()
            .SelectMany(part => part.Members.OfType<ConstructorDeclarationSyntax>())
            .Where(constructor => constructor.ParameterList.Parameters.Count == 0)
            .ToList();
    }

    private static bool AreEquivalent<T>(IReadOnlyList<T> oldNodes, IReadOnlyList<T> newNodes) where T : SyntaxNode
    {
        if (oldNodes.Count != newNodes.Count)
        {
            return false;
        }

        for (int i = 0; i < oldNodes.Count; i++)
        {
            if (!oldNodes[i].IsEquivalentTo(newNodes[i], topLevel: false))
            {
                return false;
            }
        }

        return true;
    }
}
