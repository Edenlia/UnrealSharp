using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace UnrealSharp.Shared;

/// <summary>Describes entry points reached by Unreal and by the managed plugin loader.</summary>
internal static class ManagedTrimRoots
{
    internal static XElement Read(string assemblyPath)
    {
        using FileStream Stream = File.OpenRead(assemblyPath);
        using PEReader Pe = new(Stream);
        MetadataReader Metadata = Pe.GetMetadataReader();
        string AssemblyName = Metadata.GetString(Metadata.GetAssemblyDefinition().Name);
        XElement Assembly = new("assembly", new XAttribute("fullname", AssemblyName));

        foreach (TypeDefinitionHandle Handle in Metadata.TypeDefinitions)
        {
            TypeDefinition Type = Metadata.GetTypeDefinition(Handle);
            string Name = Metadata.GetString(Type.Name);
            HashSet<string> Attributes = Type.GetCustomAttributes()
                .Select(attribute => GetAttributeName(Metadata, attribute)).ToHashSet(StringComparer.Ordinal);
            bool ReflectedType = Attributes.Overlaps(
            [
                "UnrealSharp.Attributes.UClassAttribute",
                "UnrealSharp.Attributes.UStructAttribute",
                "UnrealSharp.Attributes.UEnumAttribute",
                "UnrealSharp.Attributes.UInterfaceAttribute"
            ]);
            bool TypeNeedsAllMembers = Attributes.Contains("UnrealSharp.Attributes.UEnumAttribute")
                || Name.EndsWith("__DelegateSignature", StringComparison.Ordinal)
                || Attributes.Contains("UnrealSharp.Binds.NativeCallbacksAttribute");
            bool IsHostEntryPoint = AssemblyName == "UnrealSharp.Plugins"
                && GetTypeName(Metadata, Handle) == "UnrealSharp.Plugins.Main";

            XElement Root = new("type", new XAttribute("fullname", GetTypeName(Metadata, Handle)),
                new XAttribute("preserve", TypeNeedsAllMembers ? "all" : "nothing"));

            if (!TypeNeedsAllMembers)
            {
                foreach (string MethodName in Type.GetMethods().Select(method =>
                    Metadata.GetString(Metadata.GetMethodDefinition(method).Name)).Distinct(StringComparer.Ordinal))
                {
                    // Native dispatch can walk base types, including bases declared in other assemblies.
                    if (MethodName.StartsWith("Invoke_", StringComparison.Ordinal)
                        || (ReflectedType && MethodName is ".ctor" or "Wrap" or "ToNative")
                        || (Name == "<Module>" && MethodName == ".cctor")
                        || (IsHostEntryPoint && MethodName == "InitializeUnrealSharp"))
                    {
                        Root.Add(new XElement("method", new XAttribute("name", MethodName)));
                    }
                }
            }

            if (ReflectedType || TypeNeedsAllMembers || Root.HasElements)
            {
                Assembly.Add(Root);
            }
        }

        return Assembly;
    }

    private static string GetTypeName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        TypeDefinition Type = metadata.GetTypeDefinition(handle);
        string Name = metadata.GetString(Type.Name);
        return Type.IsNested
            ? GetTypeName(metadata, Type.GetDeclaringType()) + "/" + Name
            : Qualify(metadata.GetString(Type.Namespace), Name);
    }

    private static string GetAttributeName(MetadataReader metadata, CustomAttributeHandle handle)
    {
        EntityHandle Constructor = metadata.GetCustomAttribute(handle).Constructor;
        EntityHandle AttributeType = Constructor.Kind switch
        {
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)Constructor).Parent,
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)Constructor).GetDeclaringType(),
            _ => default
        };
        if (AttributeType.Kind == HandleKind.TypeDefinition)
        {
            return GetTypeName(metadata, (TypeDefinitionHandle)AttributeType);
        }
        if (AttributeType.Kind == HandleKind.TypeReference)
        {
            TypeReference Type = metadata.GetTypeReference((TypeReferenceHandle)AttributeType);
            return Qualify(metadata.GetString(Type.Namespace), metadata.GetString(Type.Name));
        }
        return string.Empty;
    }

    private static string Qualify(string @namespace, string name) =>
        string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
}
