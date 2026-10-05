namespace UnrealSharp.Attributes;

/// <summary>
/// Preserve native CPF_ConstParm on a value parameter. Use C# in for a readonly
/// reference; this attribute does not change C# argument passing or storage.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ConstAttribute : Attribute;
