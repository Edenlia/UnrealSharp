using System.Collections.Generic;
using EpicGames.UHT.Types;
using UnrealSharpManagedGlue.SourceGeneration;
using UnrealSharpManagedGlue.Utilities;

namespace UnrealSharpManagedGlue.Exporters;

public sealed class ExplicitInterfaceFunctionExporter : FunctionExporter
{
    public ExplicitInterfaceFunctionExporter(UhtFunction function) : base(function)
    {
        Initialize(OverloadMode.SuppressOverloads, EFunctionProtectionMode.UseUFunctionProtection);
    }

    public void Export(GeneratorStringBuilder builder)
    {
        builder.TryAddWithEditor(Function);
        string interfaceName = Function.Outer!.GetFullManagedName();
        string returnType = Function.ReturnProperty != null
            ? ReturnValueTranslator!.GetManagedType(Function.ReturnProperty)
            : "void";
        List<string> genericTypes = new();
        if (HasGenericTypeSupport)
        {
            genericTypes.Add("DOT");
        }
        if (HasCustomStructParamSupport)
        {
            genericTypes.AddRange(CustomStructParamTypes);
        }
        string genericArguments = genericTypes.Count > 0 ? $"<{string.Join(", ", genericTypes)}>" : "";
        // Explicit implementations inherit their constraints from the interface.
        builder.AppendLine($"{returnType} {interfaceName}.{FunctionName}{genericArguments}({ParamStringApiWithDefaults})");
        builder.OpenBrace();
        string returnStatement = Function.ReturnProperty != null ? "return " : "";
        builder.AppendLine($"{returnStatement}{interfaceName}.Wrap(this).{FunctionName}{genericArguments}({ParamsStringCall});");
        builder.CloseBrace();
        builder.TryEndWithEditor(Function);
    }
}
