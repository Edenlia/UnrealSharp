using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnrealSharp.Core;
using UnrealSharp.Editor.Interop;
using UnrealSharp.Plugins;
using UnrealSharp.SourceGenerator.Utilities;

namespace UnrealSharp.Editor;

public static class SyntaxUtilities
{
    public static void LookForChangesInUnrealTypes(Compilation oldCompilation, SyntaxTree? existingTree,
        Compilation newCompilation, SyntaxTree? newTree, Project owningProject)
    {
        foreach (UnrealTypeChange change in UnrealTypeChangeDetector.GetChanges(
                     oldCompilation, existingTree, newCompilation, newTree))
        {
            DirtyUnrealType(change.Declaration, owningProject, change.Flags);
        }
    }

    public static void DirtyUnrealType(BaseTypeDeclarationSyntax syntax, Project owningProject,
        ECSTypeStructuralFlags flags)
    {
        string typeNamespace = syntax.GetFullNamespace();
        string typeName = syntax.Identifier.Text;
        string assemblyName = owningProject.AssemblyName;

        string fullTypeName = string.IsNullOrEmpty(typeNamespace) ? typeName : $"{typeNamespace}.{typeName}";

        Type? runtimeType = FindLoadedType(assemblyName, fullTypeName);
        if (runtimeType == null)
        {
            Bind_FUnrealSharpEditorModule.CallNotifyNewType();
        }
        else
        {
            Bind_FUnrealSharpEditorModule.CallDirtyUnrealType(NativeReflectionHelper.GetNativeField(runtimeType),
                flags);
        }
    }
    
    private static Type? FindLoadedType(string assemblyName, string fullTypeName)
    {
        Plugin? plugin = PluginLoader.FindPlugin(assemblyName);

        if (plugin == null)
        {
            throw new ArgumentException($"Plugin {assemblyName} could not be found.");
        }
        
        Assembly assembly = (Assembly) plugin.Assembly!.Target!;
        return assembly.GetType(fullTypeName, throwOnError: false);
    }

}
