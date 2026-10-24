using System.Runtime.InteropServices;

namespace UnrealSharp.Plugins;

internal static class Main
{
    [UnmanagedCallersOnly]
    private static unsafe void InitializeUnrealSharp(
        byte* workingDirectoryUtf8,
        PluginsCallbacks* pluginCallbacks,
        nint bindsCallbacks,
        nint managedCallbacks,
        FCSInitializationResult* result)
    {
        RuntimeBootstrap.Initialize(workingDirectoryUtf8, pluginCallbacks, bindsCallbacks, managedCallbacks, result);
    }
}
