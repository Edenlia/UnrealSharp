using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UnrealSharp.Automation.Utilities;

/// <summary>How a CoreClrEmbedded package hands its managed assemblies to the runtime (ECSAssemblySource).</summary>
public enum RuntimeAssemblySource
{
    /// <summary>Assembly files on disk, passed as trusted platform assemblies.</summary>
    Files,
    /// <summary>Assemblies read through the Unreal file system and returned from external_assembly_probe.</summary>
    Probe,
}

/// <summary>
/// Writes UnrealSharp.Runtime.json, which tells the native plugin which runtime host to start for a packaged
/// build. Its format is read by FCSRuntimeManifest in UnrealSharpCore.
/// </summary>
public static class RuntimeManifestWriter
{
    public const string FileName = "UnrealSharp.Runtime.json";
    public const string LegacyNativeAotMarkerName = "UnrealSharp.NativeAOT";
    private const int Version = 1;

    /// <param name="directory">The managed output directory that is staged with the build.</param>
    /// <param name="runtime">The runtime the directory was published for.</param>
    /// <param name="library">
    /// Native library the host loads, as a platform reference: relative to the managed directory on desktop,
    /// relative to the app's Frameworks directory on iOS, and a bare file name on Android.
    /// </param>
    /// <param name="properties">Runtime properties the host passes to the runtime at startup.</param>
    /// <param name="nativeLibraries">Library references that p/invokes by these library names bind to.</param>
    /// <param name="readyToRunImages">Platform-native ReadyToRun composite images keyed by composite name.</param>
    /// <param name="assemblySource">Where the host reads managed assemblies from; Files is the default and is not written.</param>
    public static void Write(string directory, RuntimeFlavor runtime, string? library = null,
        IReadOnlyDictionary<string, string>? properties = null,
        IReadOnlyDictionary<string, string>? nativeLibraries = null,
        IReadOnlyDictionary<string, string>? readyToRunImages = null,
        RuntimeAssemblySource assemblySource = RuntimeAssemblySource.Files)
    {
        // The marker predates the manifest; a stale one must not override the manifest's runtime.
        string LegacyMarker = Path.Combine(directory, LegacyNativeAotMarkerName);
        if (File.Exists(LegacyMarker))
        {
            File.Delete(LegacyMarker);
        }

        Dictionary<string, object> Manifest = new()
        {
            ["Version"] = Version,
            ["Kind"] = runtime.ToString(),
        };
        if (!string.IsNullOrEmpty(library))
        {
            Manifest["Library"] = library.Replace('\\', '/');
        }
        if (assemblySource != RuntimeAssemblySource.Files)
        {
            Manifest["AssemblySource"] = assemblySource.ToString();
        }
        AddTable(Manifest, "Properties", properties);
        AddTable(Manifest, "NativeLibraries", nativeLibraries);
        AddTable(Manifest, "ReadyToRunImages", readyToRunImages);

        string ManifestPath = Path.Combine(directory, FileName);
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(Manifest, new JsonSerializerOptions { WriteIndented = true }));
        LoggerUtilities.LogUnrealSharpInfo($"Wrote {runtime} runtime manifest: {ManifestPath}");
    }

    private static void AddTable(Dictionary<string, object> manifest, string name, IReadOnlyDictionary<string, string>? values)
    {
        if (values is { Count: > 0 })
        {
            manifest[name] = new SortedDictionary<string, string>(values.ToDictionary(pair => pair.Key, pair => pair.Value.Replace('\\', '/')), StringComparer.Ordinal);
        }
    }
}
