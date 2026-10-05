namespace UnrealSharp.Attributes;

/// <summary>
/// Maps a byte enum to an existing UserDefinedEnum asset. This does not register
/// a new UEnum. Declare all real entries in asset order with their exact values;
/// omit Unreal's generated MAX entry. Do not combine with UEnumAttribute.
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class BlueprintEnumAttribute(string assetPath) : Attribute
{
    public string AssetPath { get; } = assetPath;
}
