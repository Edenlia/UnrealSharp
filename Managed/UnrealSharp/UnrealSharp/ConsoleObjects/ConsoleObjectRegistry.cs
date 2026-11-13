using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using UnrealSharp.Core;
using UnrealSharp.Core.Interop;
using UnrealSharp.Core.Marshallers;
using UnrealSharp.Engine;
using UnrealSharp.Interop;

namespace UnrealSharp;

/// <summary>
/// Base class of console variables and commands declared from C#.
/// </summary>
public abstract class FConsoleObject
{
    /// <summary>
    /// The name typed in the console.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// False if native registration failed (e.g. the name is already taken) or the owning assembly was unloaded.
    /// </summary>
    public bool IsRegistered => NativeObject != IntPtr.Zero;

    internal IntPtr NativeObject;
    internal readonly int Id;
    // NativeAOT console objects live for the process lifetime and need no unload owner.
    internal Assembly? OwnerAssembly;

    private protected FConsoleObject(string name, Assembly? ownerAssembly)
    {
        Name = name;
        OwnerAssembly = ownerAssembly;
        Id = ConsoleObjectRegistry.Add(this);
    }

    internal void Unregister()
    {
        if (NativeObject != IntPtr.Zero)
        {
            Bind_IConsoleManager.CallUnregister(NativeObject);
            NativeObject = IntPtr.Zero;
        }

        OwnerAssembly = null;
        OnUnregistered();
    }

    private protected virtual void OnUnregistered()
    {
    }
}

/// <summary>
/// Routes native console callbacks to the C# console objects, and unregisters them when their assembly unloads.
/// Native code only holds pointers to the static callbacks here, which live in this non-collectible assembly.
/// </summary>
internal static unsafe class ConsoleObjectRegistry
{
    private static readonly Dictionary<int, FConsoleObject> ConsoleObjects = new();
    private static readonly HashSet<AssemblyLoadContext> HookedLoadContexts = new();
    private static int _nextId;

    internal static IntPtr VariableChangedCallback => (IntPtr) (delegate* unmanaged<int, void>) &OnVariableChanged;
    internal static IntPtr CommandCallback => (IntPtr) (delegate* unmanaged<int, UnmanagedArray*, IntPtr, void>) &OnExecuteCommand;

    internal static int Add(FConsoleObject consoleObject)
    {
        int id = ++_nextId;
        ConsoleObjects.Add(id, consoleObject);

        AssemblyLoadContext? loadContext = consoleObject.OwnerAssembly is { } ownerAssembly
            ? AssemblyLoadContext.GetLoadContext(ownerAssembly) : null;
        if (loadContext is { IsCollectible: true } && HookedLoadContexts.Add(loadContext))
        {
            loadContext.Unloading += OnLoadContextUnloading;
        }

        return id;
    }

    private static void OnLoadContextUnloading(AssemblyLoadContext loadContext)
    {
        loadContext.Unloading -= OnLoadContextUnloading;
        HookedLoadContexts.Remove(loadContext);

        List<FConsoleObject> unloadingObjects = ConsoleObjects.Values
            .Where(consoleObject => consoleObject.OwnerAssembly != null && AssemblyLoadContext.GetLoadContext(consoleObject.OwnerAssembly) == loadContext)
            .ToList();

        foreach (FConsoleObject consoleObject in unloadingObjects)
        {
            consoleObject.Unregister();
            ConsoleObjects.Remove(consoleObject.Id);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnVariableChanged(int id)
    {
        try
        {
            if (ConsoleObjects.TryGetValue(id, out FConsoleObject? consoleObject) && consoleObject is IConsoleVariableChangedHandler handler)
            {
                handler.BroadcastChanged();
            }
        }
        catch (Exception exception)
        {
            LogUnrealSharp.LogError($"Console variable changed callback threw: {exception}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnExecuteCommand(int id, UnmanagedArray* nativeArgs, IntPtr nativeWorld)
    {
        try
        {
            if (!ConsoleObjects.TryGetValue(id, out FConsoleObject? consoleObject) || consoleObject is not FAutoConsoleCommand command)
            {
                return;
            }

            string[] args = nativeArgs == null ? [] : nativeArgs->ToListWithMarshaller(StringMarshaller.FromNative).ToArray();

            UWorld? world = null;
            if (nativeWorld != IntPtr.Zero)
            {
                world = GCHandleUtilities.GetObjectFromHandlePtr<UWorld>(Bind_UCSManager.CallFindManagedObject(nativeWorld));
            }

            command.Execute(args, world);
        }
        catch (Exception exception)
        {
            LogUnrealSharp.LogError($"Console command threw: {exception}");
        }
    }
}

internal interface IConsoleVariableChangedHandler
{
    void BroadcastChanged();
}

/// <summary>
/// Called by queued module initializer jobs to register console objects declared in static classes during plugin loading,
/// after native bindings and type registration are ready.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ConsoleObjectInitializer
{
    public static void RegisterConsoleObjects(Type declaringType)
    {
        try
        {
            RuntimeHelpers.RunClassConstructor(declaringType.TypeHandle);
        }
        catch (Exception exception)
        {
            // Don't fail the assembly load over a debug variable.
            LogUnrealSharp.LogError($"Failed to register console objects of {declaringType.FullName}: {exception}");
        }
    }
}
