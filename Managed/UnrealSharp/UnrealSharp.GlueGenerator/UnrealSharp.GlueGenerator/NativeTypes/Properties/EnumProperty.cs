using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using System.Linq;
using UnrealSharp.GlueGenerator.Exceptions;

namespace UnrealSharp.GlueGenerator.NativeTypes.Properties;

public record EnumProperty : FieldProperty
{
    public override string MarshallerType => $"EnumMarshaller<{ManagedType}>";
    public string? BlueprintEnumPath { get; }
    public EquatableList<byte> BlueprintEnumValues { get; }

    public EnumProperty(ISymbol symbol, ITypeSymbol typeSymbol, UnrealType outer, SyntaxNode? syntaxNode = null)
        : base(symbol, typeSymbol, PropertyType.Enum, outer, syntaxNode)
    {
        AttributeData? mapping = typeSymbol.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "UnrealSharp.Attributes.BlueprintEnumAttribute");
        if (mapping is null) return;
        if (typeSymbol is not INamedTypeSymbol { EnumUnderlyingType.SpecialType: SpecialType.System_Byte }
            || typeSymbol.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "UnrealSharp.Attributes.UEnumAttribute"))
            throw new ParseReflectionException($"{typeSymbol}: BlueprintEnum requires byte storage and cannot also have UEnum.");
        BlueprintEnumPath = mapping.ConstructorArguments.FirstOrDefault().Value as string;
        if (string.IsNullOrWhiteSpace(BlueprintEnumPath) || !BlueprintEnumPath!.StartsWith("/")
            || !BlueprintEnumPath.Contains(".") || BlueprintEnumPath.Any(char.IsWhiteSpace))
            throw new ParseReflectionException($"{typeSymbol}: BlueprintEnum requires an absolute asset object path.");
        var values = typeSymbol.GetMembers().OfType<IFieldSymbol>()
            .Where(field => field.HasConstantValue).Select(field => (byte)field.ConstantValue!).ToList();
        if (values.Count == 0 || values.Distinct().Count() != values.Count)
            throw new ParseReflectionException($"{typeSymbol}: BlueprintEnum requires nonempty, unique byte values in asset order.");
        BlueprintEnumValues = new EquatableList<byte>(values);
    }

    public override void CollectDependencies()
    {
        // The asset is owned by Unreal, not a managed assembly/type definition.
        if (BlueprintEnumPath is null) base.CollectDependencies();
    }

    public override void PopulateJsonObject(JsonWriter jsonWriter)
    {
        base.PopulateJsonObject(jsonWriter);
        if (BlueprintEnumPath is null) return;
        jsonWriter.WritePropertyName("BlueprintEnumPath");
        jsonWriter.WriteValue(BlueprintEnumPath);
        jsonWriter.WritePropertyName("BlueprintEnumValues");
        jsonWriter.WriteStartArray();
        foreach (byte value in BlueprintEnumValues) jsonWriter.WriteValue(value);
        jsonWriter.WriteEndArray();
    }
}
