using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UnrealSharp.Automation.Utilities;

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
    public static void Write(string directory, RuntimeFlavor runtime, string? library = null,
        IReadOnlyDictionary<string, string>? properties = null)
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
        if (properties is { Count: > 0 })
        {
            Manifest["Properties"] = new SortedDictionary<string, string>(new Dictionary<string, string>(properties));
        }

        string ManifestPath = Path.Combine(directory, FileName);
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(Manifest, new JsonSerializerOptions { WriteIndented = true }));
        LoggerUtilities.LogUnrealSharpInfo($"Wrote {runtime} runtime manifest: {ManifestPath}");
    }
}
