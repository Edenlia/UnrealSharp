using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Xml.Linq;

namespace UnrealSharp.Automation.Utilities;

// The packaged IL contains both UHT glue and Roslyn-generated registration code. Read their
// existing metadata so installed and source builds use the same rules without loading the DLLs.
internal static class UnrealSharpTrimmingRoots
{
    private const string AttributeNamespace = "UnrealSharp.Attributes.";

    // Keep these suppressions at the native boundary. The roots below and the published assembly
    // set account for these name-based calls. Warnings from game code remain errors.
    private static readonly (string Assembly, string Type, string Method, string[] Warnings)[] NativeLookupWarnings =
    [
        ("UnrealSharp", "UnrealSharp.TDelegateBase`1", ".cctor", ["IL2026"]),
        ("UnrealSharp", "UnrealSharp.TDelegateBase`1", ".ctor", ["IL2077"]),
        ("UnrealSharp", "UnrealSharp.Interop.ExportedFunctionsManager", "Initialize", ["IL2026", "IL2075"]),
        ("UnrealSharp.Core", "UnrealSharp.Core.UnmanagedCallbacks", "CreateNewManagedObjectWrapper", ["IL2075"]),
        ("UnrealSharp.Core", "UnrealSharp.Core.UnmanagedCallbacks", "GetManagedMethod", ["IL2075"]),
        ("UnrealSharp.Core", "UnrealSharp.Core.UnmanagedCallbacks", "GetManagedTypeHandle", ["IL2026"]),
        ("UnrealSharp.Core", "UnrealSharp.Core.UnmanagedCallbacks", "InitializeStruct", ["IL2072", "IL2075"]),
        ("UnrealSharp.Core", "UnrealSharp.Core.UnrealSharpObject", "Create", ["IL2067", "IL2070"]),
        ("UnrealSharp.Plugins", "UnrealSharp.Plugins.PluginLoadContext", "Load", ["IL2026"]),
    ];

    internal static XElement? CreateLinkAttributes(MetadataReader metadata, string assemblyName)
    {
        var Rules = NativeLookupWarnings.Where(Rule => Rule.Assembly == assemblyName).ToLookup(Rule => Rule.Type);
        if (Rules.Count == 0)
        {
            return null;
        }

        XElement Assembly = new("assembly", new XAttribute("fullname", assemblyName));
        foreach (TypeDefinitionHandle Handle in metadata.TypeDefinitions)
        {
            string TypeName = GetTypeName(metadata, Handle);
            if (!Rules.Contains(TypeName))
            {
                continue;
            }

            HashSet<string> Methods = new(metadata.GetTypeDefinition(Handle).GetMethods().Select(Method =>
                metadata.GetString(metadata.GetMethodDefinition(Method).Name)), StringComparer.Ordinal);
            XElement Type = new("type", new XAttribute("fullname", TypeName));
            foreach (var Rule in Rules[TypeName].Where(Rule => Methods.Contains(Rule.Method)))
            {
                XElement Method = new("method", new XAttribute("name", Rule.Method));
                foreach (string Warning in Rule.Warnings)
                {
                    Method.Add(new XElement("attribute",
                        new XAttribute("fullname", "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute"),
                        new XAttribute("assembly", "System.Private.CoreLib"),
                        new XElement("argument", "Trimming"), new XElement("argument", Warning)));
                }
                Type.Add(Method);
            }

            Assembly.Add(Type);
        }

        return Assembly.HasElements ? Assembly : null;
    }

    internal static XElement CreateAssemblyDescriptor(MetadataReader metadata)
    {
        string AssemblyName = metadata.GetString(metadata.GetAssemblyDefinition().Name);
        XElement Assembly = new("assembly", new XAttribute("fullname", AssemblyName),
            new XElement("type", new XAttribute("fullname", "<Module>"), new XAttribute("preserve", "all")));

        foreach (TypeDefinitionHandle Handle in metadata.TypeDefinitions)
        {
            TypeDefinition Definition = metadata.GetTypeDefinition(Handle);
            string Name = metadata.GetString(Definition.Name);
            HashSet<string> Attributes = GetAttributeNames(metadata, Definition.GetCustomAttributes());
            bool IsClass = Attributes.Contains(AttributeNamespace + "UClassAttribute");
            bool IsInterface = Attributes.Contains(AttributeNamespace + "UInterfaceAttribute");
            bool IsStruct = Attributes.Contains(AttributeNamespace + "UStructAttribute");
            bool HasNativeFields = IsStruct || Attributes.Contains(AttributeNamespace + "UEnumAttribute");
            bool IsCallbackTable = Attributes.Contains("UnrealSharp.Binds.NativeCallbacksAttribute");
            bool IsDelegateWrapper = Name.EndsWith("__DelegateSignature", StringComparison.Ordinal);
            bool IsDelegate = Attributes.Contains(AttributeNamespace + "UMultiDelegateAttribute") ||
                              Attributes.Contains(AttributeNamespace + "USingleDelegateAttribute");

            // Native-created objects, script interfaces and delegate wrappers are reached by name.
            // The callback table is itself enumerated by reflection during startup.
            XElement Type = new("type", new XAttribute("fullname", GetTypeName(metadata, Handle)),
                new XAttribute("preserve", IsCallbackTable || IsDelegateWrapper || IsDelegate ? "all" :
                    HasNativeFields ? "fields" : "nothing"));

            if (IsCallbackTable || IsDelegateWrapper || IsDelegate)
            {
                Assembly.Add(Type);
                continue;
            }

            HashSet<string> Methods = new(StringComparer.Ordinal);
            foreach (MethodDefinitionHandle MethodHandle in Definition.GetMethods())
            {
                MethodDefinition Method = metadata.GetMethodDefinition(MethodHandle);
                string MethodName = metadata.GetString(Method.Name);
                HashSet<string> MethodAttributes = GetAttributeNames(metadata, Method.GetCustomAttributes());
                if (((IsClass || IsStruct) && MethodName == ".ctor") ||
                    (IsInterface && MethodName == "Wrap") || (IsStruct && MethodName == "ToNative") ||
                    MethodName.StartsWith("Invoke_", StringComparison.Ordinal) ||
                    (MethodAttributes.Contains(AttributeNamespace + "UFunctionAttribute") &&
                     !MethodAttributes.Contains("UnrealSharp.Core.Attributes.GeneratedFunctionAttribute")) ||
                    MethodAttributes.Contains("System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute"))
                {
                    Methods.Add(MethodName);
                }
            }

            Type.Add(Methods.Order(StringComparer.Ordinal).Select(Method =>
                new XElement("method", new XAttribute("name", Method))));
            foreach (PropertyDefinitionHandle PropertyHandle in Definition.GetProperties())
            {
                PropertyDefinition Property = metadata.GetPropertyDefinition(PropertyHandle);
                if (GetAttributeNames(metadata, Property.GetCustomAttributes()).Contains(AttributeNamespace + "UPropertyAttribute"))
                {
                    Type.Add(new XElement("property", new XAttribute("name", metadata.GetString(Property.Name))));
                }
            }

            foreach (FieldDefinitionHandle FieldHandle in Definition.GetFields())
            {
                FieldDefinition Field = metadata.GetFieldDefinition(FieldHandle);
                if (GetAttributeNames(metadata, Field.GetCustomAttributes()).Contains(AttributeNamespace + "UPropertyAttribute"))
                {
                    Type.Add(new XElement("field", new XAttribute("name", metadata.GetString(Field.Name))));
                }
            }

            if (IsClass || IsInterface || HasNativeFields || Type.HasElements)
            {
                Assembly.Add(Type);
            }
        }

        return Assembly;
    }

    internal static string? GetAttributeName(MetadataReader metadata, CustomAttributeHandle handle)
    {
        EntityHandle Constructor = metadata.GetCustomAttribute(handle).Constructor;
        if (Constructor.Kind == HandleKind.MethodDefinition)
        {
            return GetTypeName(metadata, metadata.GetMethodDefinition((MethodDefinitionHandle)Constructor).GetDeclaringType());
        }

        if (Constructor.Kind == HandleKind.MemberReference)
        {
            EntityHandle Parent = metadata.GetMemberReference((MemberReferenceHandle)Constructor).Parent;
            if (Parent.Kind == HandleKind.TypeReference)
            {
                TypeReference Reference = metadata.GetTypeReference((TypeReferenceHandle)Parent);
                return Qualify(metadata.GetString(Reference.Namespace), metadata.GetString(Reference.Name));
            }
        }

        return null;
    }

    private static HashSet<string> GetAttributeNames(MetadataReader metadata, CustomAttributeHandleCollection handles) =>
        new(handles.Select(Handle => GetAttributeName(metadata, Handle)).OfType<string>(), StringComparer.Ordinal);

    private static string GetTypeName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        TypeDefinition Definition = metadata.GetTypeDefinition(handle);
        string Name = metadata.GetString(Definition.Name);
        TypeDefinitionHandle DeclaringType = Definition.GetDeclaringType();
        return DeclaringType.IsNil ? Qualify(metadata.GetString(Definition.Namespace), Name) :
            GetTypeName(metadata, DeclaringType) + "/" + Name;
    }

    private static string Qualify(string ns, string name) => string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}
