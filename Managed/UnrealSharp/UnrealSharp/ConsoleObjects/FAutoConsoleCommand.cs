using System.Reflection;
using System.Runtime.CompilerServices;
using UnrealSharp.Engine;
using UnrealSharp.Interop;

namespace UnrealSharp;

/// <summary>
/// A console command declared from C#, the equivalent of FAutoConsoleCommand in C++.
/// </summary>
/// <remarks>
/// Declare it as a static readonly field of a static class, so it is registered as soon as the assembly loads:
/// <code>
/// public static class MyGameCommands
/// {
///     public static readonly FAutoConsoleCommand Teleport =
///         new("MyGame.Teleport", "MyGame.Teleport X Y Z", (string[] args, UWorld? world) => { ... });
/// }
/// </code>
/// Fields declared in other types are registered when that type is first used.
/// The command runs on the game thread with its world as the current world context.
/// </remarks>
public sealed class FAutoConsoleCommand : FConsoleObject
{
    private Action<string[], UWorld?>? _command;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public FAutoConsoleCommand(string name, string help, Action command, EConsoleVariableFlags flags = EConsoleVariableFlags.Default)
        : base(name, RuntimeFeature.IsDynamicCodeSupported ? Assembly.GetCallingAssembly() : null)
    {
        _command = (_, _) => command();
        NativeObject = Register(help, flags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public FAutoConsoleCommand(string name, string help, Action<string[]> command, EConsoleVariableFlags flags = EConsoleVariableFlags.Default)
        : base(name, RuntimeFeature.IsDynamicCodeSupported ? Assembly.GetCallingAssembly() : null)
    {
        _command = (args, _) => command(args);
        NativeObject = Register(help, flags);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public FAutoConsoleCommand(string name, string help, Action<string[], UWorld?> command, EConsoleVariableFlags flags = EConsoleVariableFlags.Default)
        : base(name, RuntimeFeature.IsDynamicCodeSupported ? Assembly.GetCallingAssembly() : null)
    {
        _command = command;
        NativeObject = Register(help, flags);
    }

    internal void Execute(string[] args, UWorld? world)
    {
        _command?.Invoke(args, world);
    }

    private unsafe IntPtr Register(string help, EConsoleVariableFlags flags)
    {
        fixed (char* nativeName = Name)
        fixed (char* nativeHelp = help ?? string.Empty)
        fixed (char* nativeAssemblyName = OwnerAssembly?.GetName().Name ?? string.Empty)
        {
            return Bind_IConsoleManager.CallRegisterCommand(nativeName, nativeHelp, (uint) flags, nativeAssemblyName,
                ConsoleObjectRegistry.CommandCallback, Id);
        }
    }

    private protected override void OnUnregistered()
    {
        _command = null;
    }
}
