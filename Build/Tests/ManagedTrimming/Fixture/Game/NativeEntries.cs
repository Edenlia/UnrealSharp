using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using UnrealSharp.Attributes;
using UnrealSharp.Binds;

namespace Game;

[UClass]
internal class Actor
{
    [UProperty]
    public int State { get; private set; }

    private Actor() => State = 40;
    private int Invoke_Started() => State + Registration.Count;
    public int Unused() => 999;

    [UFunction]
    [DynamicDependency(nameof(ReflectionTarget.Read), typeof(ReflectionTarget))]
    public static string ReadResource()
    {
        using StreamReader Reader = new(typeof(Actor).Assembly.GetManifestResourceStream("Game.Fixture")!);
        return Reader.ReadToEnd().Trim();
    }

#if UNSAFE_REFLECTION
    [UFunction]
    public static Type? LoadType(string name) => typeof(Actor).Assembly.GetType(name);
#endif
}

internal static class Registration
{
    internal static int Count;

#pragma warning disable CA2255 // This represents the initializer emitted by the glue generator.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install() => Count = 2;
}

[UStruct]
internal struct Payload
{
    private int _value;

    public Payload() => _value = 42;
    public int ToNative() => _value;
}

[UEnum]
internal enum Mode
{
    Active = 42
}

[UInterface]
internal interface IWrapper
{
    public static IWrapper Wrap(int value) => new Wrapper(value);
    [UFunction]
    int Value();
}

internal sealed class Wrapper(int value) : IWrapper
{
    public int Value() => value;
}

[NativeCallbacks]
internal static class Callbacks
{
    public static nint Pointer = 0;
}

internal sealed class Callback__DelegateSignature
{
    public int Invoke() => 42;
}

internal static class ReflectionTarget
{
    internal static int Read() => 42;
    public static int Unused() => 0;
}

public static class PluginDependency
{
    public static int Read() => 42;
}
