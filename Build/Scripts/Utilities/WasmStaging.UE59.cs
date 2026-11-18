using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;

namespace UnrealSharp.Automation.Utilities;

internal static partial class UnrealSharpStaging
{
    static partial void StageWasm(ProjectParams parameters, DeploymentContext context, TargetReceipt receipt, ref bool handled)
    {
        if (!WasmPrelink.IsWasm(receipt.Platform)) return;
        WasmStagingProduct.Stage(parameters, context, receipt);
        handled = true;
    }

    static partial void VerifyWasmPackage(ProjectParams parameters, DeploymentContext context)
    {
        if (WasmPrelink.IsWasm(context.StageTargetPlatform.PlatformType))
            WasmStagingProduct.VerifyPackage(parameters, context);
    }
}

// A running preparation owns this registration. A separate staging/restaging
// command cannot silently reuse a managed directory from an earlier native link.
internal sealed class WasmStagingProduct(string projectFile, string pluginFile, string targetName,
    UnrealTargetConfiguration configuration, string managedDirectory, string readyKey, Action requirePrepared, bool prelinked = false)
{
    private readonly string ProjectFile = projectFile, PluginFile = pluginFile, TargetName = targetName;
    private readonly UnrealTargetConfiguration Configuration = configuration;
    private readonly string ManagedDirectory = managedDirectory, ReadyKey = readyKey;
    private readonly bool Prelinked = prelinked;
    private readonly Action RequirePrepared = requirePrepared;
    private static WasmStagingProduct? _current;
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Dictionary<string, string> _nativeHashes = new(Paths);
    private Dictionary<string, string>? _nativeStageFiles;
    private string? _nativeStageDirectory;
    private Dictionary<string, string>? _managedHashes;
    private string? _stageRoot;
    private bool _packageVerified;

    internal static void RequireCompleted()
    {
        WasmStagingProduct product = _current ?? throw new AutomationException("No prepared UnrealSharp Wasm product was registered.");
        product.RequirePrepared();
        if (!product._packageVerified) throw new AutomationException("The normal UE package hook did not validate the prepared UnrealSharp Wasm product.");
    }

    internal static void Register(WasmStagingProduct product)
    {
        if (_current != null) throw new AutomationException("Only one UnrealSharp Wasm prepared game can be staged per invocation.");
        product.RequirePrepared();
        _current = product;
    }

    internal static void Stage(ProjectParams parameters, DeploymentContext context, TargetReceipt receipt)
    {
        WasmStagingProduct product = RequireCurrent(parameters, context, receipt);
        product.RequirePrepared();
        string[] files = Directory.GetFiles(product.ManagedDirectory, "*", SearchOption.TopDirectoryOnly);
        if (Directory.GetDirectories(product.ManagedDirectory).Length != 0 || files.Length == 0 ||
            (product.Prelinked ? files.Any(path => Path.GetExtension(path) != ".json") :
                !files.Any(path => Path.GetFileName(path) == "System.Private.CoreLib.dll") ||
                !files.Any(path => Path.GetFileName(path) == "UnrealSharp.Plugins.dll")) ||
            !files.Any(path => Path.GetFileName(path) == "UnrealSharp.Runtime.json"))
            throw new AutomationException("UnrealSharp Wasm staging requires the flat, complete prepared loose-PE directory.");
        if (files.Any(file => Path.GetExtension(file) is not (".dll" or ".json")))
            throw new AutomationException("Unexpected file in the prepared Wasm managed directory.");

        product._managedHashes = files.ToDictionary(path => Path.GetFileName(path), Hash, StringComparer.Ordinal);
        product._stageRoot = StagedDirectoryReference.Combine(context.RelativeProjectRootForStage, "Managed", "CoreCLR").ToString();
        foreach (string file in files)
        {
            context.StageFile(StagedFileType.NonUFS, new FileReference(file),
                new StagedFileReference(product._stageRoot + "/" + Path.GetFileName(file)));
        }

        // Capture products only after the normal UE native build succeeded. The
        // prelink Ready deliberately cannot certify a native binary not built yet.
        // UE's Wasm receipt lists the JavaScript executable; its platform
        // staging code also takes the .wasm sibling written by that link.
        string[] executables = receipt.BuildProducts.Where(native => native.Path.HasExtension(".js")).Select(native => native.Path.FullName).ToArray();
        if (executables.Length != 1) throw new AutomationException("The prepared Wasm Game needs one JavaScript executable in its native receipt.");
        foreach (string path in receipt.BuildProducts.Select(native => native.Path.FullName).Append(Path.ChangeExtension(executables[0], ".wasm")).Distinct(Paths))
        {
            if (!File.Exists(path)) throw new AutomationException("Missing native receipt product: {0}", path);
            string hash = Hash(path);
            if (product._nativeHashes.TryGetValue(path, out string? previous) && previous != hash)
                throw new AutomationException("Native receipt product changed between staging calls: {0}", path);
            product._nativeHashes[path] = hash;
        }
        if (!product._nativeHashes.Keys.Any(path => path.EndsWith(".wasm", StringComparison.Ordinal)))
            throw new AutomationException("The Wasm receipt has no actual final .wasm build product.");
        product.RequirePrepared();
    }

    internal static void ObserveStageCopy(ProjectParams parameters, DeploymentContext context)
    {
        TargetReceipt[] receipts = context.StageTargets.Select(target => target.Receipt).Where(UnrealSharpStaging.UsesUnrealSharp).ToArray();
        if (receipts.Length != 1) throw new AutomationException("UnrealSharp Wasm stage copy needs one prepared native receipt.");
        WasmStagingProduct product = RequireCurrent(parameters, context, receipts[0]);
        product.RequirePrepared();
        if (product._managedHashes == null || product._stageRoot == null || product._nativeHashes.Count == 0)
            throw new AutomationException("UnrealSharp Wasm stage copy did not follow this invocation's staging hook.");

        string stage = context.StageDirectory.FullName;
        var mappings = context.FilesToStage.NonUFSFiles.Concat(context.FilesToStage.NonUFSSystemFiles).ToArray();
        var files = new Dictionary<string, string>(Paths);
        foreach (var native in product._nativeHashes.Where(file => Path.GetExtension(file.Key) is ".js" or ".wasm"))
        {
            string[] locations = mappings.Where(entry => Paths.Equals(entry.Value.FullName, native.Key))
                .Select(entry => entry.Key.ToString()).Distinct(StringComparer.Ordinal).ToArray();
            if (locations.Length != 1) throw new AutomationException("The copied stage has no unique native product mapping: {0}", native.Key);
            if (Hash(native.Key) != native.Value || Hash(Path.Combine(stage, locations[0])) != native.Value)
                throw new AutomationException("The native staging copy differs from the successful link: {0}", native.Key);
            files.Add(native.Key, locations[0]);
        }
        if (product._nativeStageFiles != null && (!Paths.Equals(stage, product._nativeStageDirectory) ||
            product._nativeStageFiles.Count != files.Count || files.Any(file =>
                !product._nativeStageFiles.TryGetValue(file.Key, out string? previous) || previous != file.Value)))
            throw new AutomationException("The native stage directory or product mapping changed between copy callbacks.");
        product._nativeStageFiles = files;
        product._nativeStageDirectory = stage;
    }

    internal static void VerifyPackage(ProjectParams parameters, DeploymentContext context)
    {
        TargetReceipt[] receipts = context.StageTargets.Select(target => target.Receipt).Where(UnrealSharpStaging.UsesUnrealSharp).ToArray();
        if (receipts.Length != 1) throw new AutomationException("UnrealSharp Wasm package needs one prepared native receipt.");
        WasmStagingProduct product = RequireCurrent(parameters, context, receipts[0]);
        product.RequirePrepared();
        if (product._managedHashes == null || product._stageRoot == null || product._nativeHashes.Count == 0)
            throw new AutomationException("UnrealSharp Wasm package was not staged from this invocation's prepared product.");
        foreach (var native in product._nativeHashes)
            if (Hash(native.Key) != native.Value) throw new AutomationException("Native product changed after staging: {0}", native.Key);

        string stage = context.StageDirectory.FullName;
        // Package constructs a fresh DeploymentContext. Use only the final
        // mappings captured by this invocation's actual post-copy callback.
        if (product._nativeStageFiles == null || !Paths.Equals(stage, product._nativeStageDirectory))
            throw new AutomationException("UnrealSharp Wasm package has no matching observed staging copy.");
        foreach (var native in product._nativeHashes.Where(file => Path.GetExtension(file.Key) is ".js" or ".wasm"))
        {
            if (!product._nativeStageFiles.TryGetValue(native.Key, out string? location))
                throw new AutomationException("The final stage has no captured native product mapping: {0}", native.Key);
            string stagedNative = Path.Combine(stage, location);
            if (Hash(stagedNative) != native.Value) throw new AutomationException("The staged native executable differs from the successful link: {0}", stagedNative);
            if (File.Exists(stagedNative + ".br"))
            {
                using var stream = new BrotliStream(File.OpenRead(stagedNative + ".br"), CompressionMode.Decompress);
                if (Convert.ToHexString(SHA256.HashData(stream)) != native.Value)
                    throw new AutomationException("The staged native Brotli executable differs from the successful link: {0}", stagedNative);
            }
        }
        string manifestPath = Path.Combine(stage, "wasm-manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        if (manifest.RootElement.ValueKind != JsonValueKind.Array) throw new AutomationException("Invalid final Wasm file manifest.");
        var entries = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
        {
            string path = entry.GetProperty("path").GetString() ?? "";
            if (!entries.TryAdd(path, entry)) throw new AutomationException("Duplicate final Wasm virtual path: {0}", path);
        }
        string managedStage = Path.Combine(stage, product._stageRoot);
        string[] actual = Directory.GetFiles(managedStage, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".br", StringComparison.Ordinal)).Select(path => Path.GetFileName(path)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (Directory.GetDirectories(managedStage).Length != 0 || !actual.SequenceEqual(product._managedHashes.Keys.OrderBy(name => name, StringComparer.Ordinal)))
            throw new AutomationException("Final Wasm managed files differ from the prepared closure.");
        foreach (var managed in product._managedHashes)
        {
            string relative = product._stageRoot + "/" + managed.Key;
            string file = Path.Combine(stage, relative);
            if (relative.Contains("/Binaries/", StringComparison.Ordinal) || Hash(file) != managed.Value ||
                !entries.TryGetValue("/" + relative, out JsonElement entry) || entry.GetProperty("url").GetString() != relative ||
                entry.GetProperty("size").GetInt64() != new FileInfo(file).Length ||
                !string.Equals(entry.GetProperty("sha1").GetString(), Sha1(file), StringComparison.Ordinal))
                throw new AutomationException("Final Wasm manifest does not fetch-mount the matching prepared file: {0}", relative);
            string brotli = file + ".br";
            if (File.Exists(brotli))
            {
                using var stream = new BrotliStream(File.OpenRead(brotli), CompressionMode.Decompress);
                if (Convert.ToHexString(SHA256.HashData(stream)) != managed.Value)
                    throw new AutomationException("Final Wasm Brotli file differs from the prepared PE: {0}", brotli);
            }
        }
        product.RequirePrepared();
        // Evidence only, outside the platform's runtime file manifest. This is
        // written after package validation and before normal Archive is allowed.
        File.WriteAllText(Path.Combine(stage, "unrealsharp-package.json"), JsonSerializer.Serialize(new {
            Schema = "UnrealSharp.WasmPackage", SchemaVersion = 1, ReadyKey = product.ReadyKey,
            ProjectFile = product.ProjectFile, TargetName = product.TargetName, Configuration = product.Configuration.ToString(),
            Architecture = "wasm64", ManagedVirtualDirectory = "/" + product._stageRoot,
            WasmManifestSha256 = Hash(manifestPath), NativeFiles = product._nativeHashes,
            ManagedFiles = product._managedHashes
        }, new JsonSerializerOptions { WriteIndented = true }));
        product._packageVerified = true;
    }

    private static WasmStagingProduct RequireCurrent(ProjectParams parameters, DeploymentContext context, TargetReceipt receipt)
    {
        WasmStagingProduct product = _current ?? throw new AutomationException("Build UnrealSharp Wasm through BuildCookRunUnrealSharp before staging. Separate restaging is not supported yet.");
        FileReference[] plugins = receipt.RuntimeDependencies.Where(dependency => dependency.Path.GetFileName().Equals("UnrealSharp.uplugin", StringComparison.OrdinalIgnoreCase))
            .Select(dependency => dependency.Path).ToArray();
        if (!parameters.Build || !parameters.Stage || !parameters.Package || parameters.HasDLCName ||
            !Paths.Equals(context.RawProjectPath.FullName, product.ProjectFile) || receipt.ProjectFile == null ||
            !Paths.Equals(receipt.ProjectFile.FullName, product.ProjectFile) || receipt.TargetName != product.TargetName ||
            receipt.TargetType != TargetType.Game || !WasmPrelink.IsWasm(receipt.Platform) || receipt.Configuration != product.Configuration ||
            receipt.Architectures.Architectures.Count != 1 || receipt.Architectures.SingleArchitecture.ToString() != "wasm64" ||
            plugins.Length != 1 || !Paths.Equals(plugins[0].FullName, product.PluginFile))
            throw new AutomationException("The native receipt/staging request does not match the prepared UnrealSharp Wasm Game profile.");
        return product;
    }

    private static string Hash(string path)
    {
        if (Path.GetExtension(path).Equals(".uasset", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".umap", StringComparison.OrdinalIgnoreCase))
            throw new AutomationException("Unreal assets are not native or managed preparation inputs: {0}", path);
        using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static string Sha1(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant(); }
}
