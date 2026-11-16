using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildBase;
using UnrealSharp.Wasm;

namespace UnrealSharp.Automation.Utilities;

internal sealed class WasmPreparationSession
{
    private readonly WasmBuildInput _input;
    private readonly WasmPreparationTools _tools;
    private readonly string _pluginFile;

    private WasmPreparationSession(WasmBuildInput input, WasmPreparationTools tools, string pluginFile)
    {
        _input = input; _tools = tools; _pluginFile = pluginFile;
    }

    internal static void ValidateExplicitProducts(BuildCommand command)
    {
        WasmRuntimeProduct.OpenPortable(RequiredFile(command, "UnrealSharpRuntimeProduct"));
        ReadTools(command);
    }

    internal static WasmPreparationSession Create(BuildCommand command, WasmGameBuildRequest request, ProjectParams parameters)
    {
        if (!OperatingSystem.IsWindows() || !parameters.Cook || !parameters.Stage || !parameters.Package || parameters.SkipStage || parameters.SkipPackage ||
            (request.RequestedArchitectures != null && request.RequestedArchitectures != "wasm64"))
            throw new AutomationException("UnrealSharp Wasm preparation currently requires a Windows-hosted, single wasm64 Game build/cook/stage/package invocation.");
        WasmPreparationTools tools = ReadTools(command);
        WasmRuntimeProduct product = WasmRuntimeProduct.OpenPortable(RequiredFile(command, "UnrealSharpRuntimeProduct"));
        FileReference plugin = command.GetUnrealSharpUPlugin();
        var profile = new WasmBuildProfile(request.ProjectFile.FullName, plugin.Directory.FullName, Unreal.EngineDirectory.FullName,
            request.TargetName, "Game", "Wasm", request.Configuration.ToString(), "wasm64", LowerMemory64: true, Simd128: true);
        WasmBuildInput input = WasmBuildInput.Create(profile, product);
        input.WriteAtomic();
        using IDisposable preparation = input.AcquirePreparationLock();
        // Never leave a previously certified archive readable as Ready while a
        // new preparation can modify its managed inputs or native helper output.
        input.RequireSafePath(input.PreparedReadyFile);
        if (File.Exists(input.PreparedReadyFile))
            File.Move(input.PreparedReadyFile, input.PreparedReadyFile + ".previous." + Guid.NewGuid().ToString("N"));
        string template = Path.Combine(profile.PluginDirectory, "Build", "Runtime", "Wasm", "wasm-bootstrap.js.in");
        string bootstrap = File.ReadAllText(template).Replace("@UNREALSHARP_VERIFIED_JS_GIT_HASH@", product.EmittedGitHash, StringComparison.Ordinal);
        if (bootstrap.Contains("@UNREALSHARP_", StringComparison.Ordinal)) throw new AutomationException("Unresolved Wasm bootstrap product substitution.");
        input.WriteFileAtomic(input.BootstrapFile, Encoding.UTF8.GetBytes(bootstrap));
        // This is a newly authored consumer input, not a replay of a runtime or
        // UE response file. SDK C++/math aliases stay owned by the normal driver.
        string response = "--start-group\n" + QuoteLinkPath(input.AppArchive) + "\n" +
            string.Join("\n", product.LinkSequence.Where(item => item.Kind == "Archive").Select(item => QuoteLinkPath(item.Value))) + "\n--end-group\n";
        input.WriteFileAtomic(input.LinkResponse, Encoding.UTF8.GetBytes(response));
        input.RequireUnchanged();
        return new WasmPreparationSession(input, tools, plugin.FullName);
    }

    internal void PrepareManagedInputs()
    {
        using IDisposable preparation = _input.AcquirePreparationLock();
        _input.RequireUnchanged();
        WasmBuildProfile profile = _input.Profile;
        string manifestFile = Path.Combine(Path.GetDirectoryName(profile.ProjectFile)!, "Intermediate", "Build", "Wasm", profile.TargetName, profile.TargetName + ".uhtmanifest");
        string depsFile = Path.ChangeExtension(manifestFile, ".deps");
        _input.RequireSafePath(manifestFile); _input.RequireSafePath(depsFile);
        string[] dependencies = File.ReadAllLines(depsFile).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => Path.GetFullPath(line.Trim().TrimStart('\uFEFF'))).ToArray();
        string Tool(string name)
        {
            string[] matches = dependencies.Where(path => Path.GetFileName(path) == name).Distinct(WasmData.Paths).ToArray();
            return matches.Length == 1 ? matches[0] : throw new AutomationException("Actual UHT dependencies must identify exactly one {0}.", name);
        }
        var request = new UhtProducerReadRequest(manifestFile, profile.ProjectFile, profile.PluginDirectory, profile.EngineDirectory,
            profile.TargetName, profile.Platform, Enum.Parse<UnrealBuildTool.UnrealTargetConfiguration>(profile.Configuration), profile.Architecture,
            Tool("UnrealSharpManagedGlue.dll"), Tool("UnrealSharp.Automation.dll"));
        WasmPreparedApp prepared;
        // Existing shared read locks stay held through managed/helper generation.
        // Leaving this scope releases them before normal BCR invokes UBT again.
        using (UhtProducerSnapshot producer = UhtProducerReader.Read(request))
        {
            var observed = new Dictionary<string, WasmPreparedFile>(WasmData.Paths);
            void Observe(string path)
            {
                path = Path.GetFullPath(path); _input.RequireSafePath(path);
                if (Path.GetExtension(path).Equals(".uasset", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".umap", StringComparison.OrdinalIgnoreCase))
                    throw new AutomationException("Unreal assets are not Wasm prelink inputs: {0}", path);
                using FileStream stream = File.OpenRead(path);
                var file = new WasmPreparedFile(path, stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
                if (observed.TryGetValue(path, out WasmPreparedFile? previous) && previous != file)
                    throw new AutomationException("Wasm preparation input changed: {0}", path);
                observed[path] = file;
            }
            foreach (string path in producer.FileHashes.Keys) Observe(path);
            foreach (var root in producer.Inventories)
                foreach (string relative in root.Value.Keys) Observe(Path.Combine(root.Key, relative));
            UHTManifest manifest = JsonSerializer.Deserialize<UHTManifest>(File.ReadAllBytes(manifestFile))
                ?? throw new AutomationException("Missing actual Wasm UHT manifest.");
            // Inputs is an opaque writer fingerprint. Freeze the actual parsed
            // header bytes separately for this application's final-link guard.
            foreach (UHTManifest.Module module in manifest.Modules)
                foreach (string header in module.ClassesHeaders.Concat(module.PublicHeaders).Concat(module.InternalHeaders).Concat(module.PrivateHeaders)) Observe(header);
            foreach (string path in new[] { _input.InputFile, _input.LinkResponse, _input.BootstrapFile,
                Path.Combine(profile.PluginDirectory, "Build", "Runtime", "Wasm", "wasm-bootstrap.js.in"),
                Path.Combine(profile.PluginDirectory, "Build", "Runtime", "Wasm", "unrealsharp-wasm-library.js") }) Observe(path);

            WasmManagedPreparationResult result = WasmManagedPreparation.Build(_input, producer, _tools);
            string runtimeManifest = Path.Combine(result.ManagedDirectory, "UnrealSharp.Runtime.json");
            _input.WriteFileAtomic(runtimeManifest, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                Version = 1, Kind = "CoreClrEmbedded", Library = "", Properties = new Dictionary<string, string>(),
                NativeLibraries = new Dictionary<string, string>(), ReadyToRunImages = new Dictionary<string, string>()
            })));
            foreach (string path in result.Files.Append(runtimeManifest).Append(_input.AppArchive)) Observe(path);
            producer.RequireUnchanged();
            // Re-read every pre-build observation; hashing changed source only
            // after compilation cannot certify what the compiler consumed.
            foreach (string path in observed.Keys.ToArray()) Observe(path);
            var payload = new WasmPreparedPayload(profile, _input.NativeProductKey, _input.RuntimeProductSha256,
                new WasmUhtIdentity(manifestFile, producer.ManifestHash, producer.Context, producer.Inputs, producer.Transaction),
                result.ManagedDirectory, runtimeManifest, observed.Values.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray());
            prepared = WasmPreparedApp.WriteAtomic(_input, payload);
        }
        prepared.RequireUnchanged();
        WasmStagingProduct.Register(new WasmStagingProduct(profile.ProjectFile, _pluginFile, profile.TargetName,
            request.Configuration, prepared.ManagedDirectory, prepared.ReadyKey, prepared.RequireUnchanged));
    }

    internal IDisposable AcquireBuildLease()
    {
        IDisposable lease = _input.AcquireBuildLease();
        try { WasmPreparedApp.Load(_input).RequireUnchanged(); return lease; }
        catch { lease.Dispose(); throw; }
    }

    private static WasmPreparationTools ReadTools(BuildCommand command)
    {
        string sdk = RequiredDirectory(command, "UnrealSharpWasmSdk");
        string selected = Environment.GetEnvironmentVariable("WASM_EMSDK_ROOT") ?? Environment.GetEnvironmentVariable("EMSDK") ?? "";
        if (selected.Length == 0 || !WasmData.Paths.Equals(Path.GetFullPath(selected), sdk))
            throw new AutomationException("-UnrealSharpWasmSdk must match the normal UE-selected WASM_EMSDK_ROOT/EMSDK. Select the SDK before starting UAT.");
        string config = Path.Combine(sdk, ".emscripten"), llvm = Path.Combine(sdk, "upstream", "bin");
        string cache = RequiredDirectory(command, "UnrealSharpWasmEmCache");
        string? selectedCache = Environment.GetEnvironmentVariable("EM_CACHE");
        if (!File.Exists(config) || !Directory.Exists(llvm) || string.IsNullOrWhiteSpace(selectedCache) ||
            !WasmData.Paths.Equals(Path.GetFullPath(selectedCache), cache))
            throw new AutomationException("UnrealSharp's explicit emsdk config/cache must match the normal UE toolchain environment.");
        string? overrideLlvm = Environment.GetEnvironmentVariable("EM_LLVM_ROOT");
        if (!string.IsNullOrEmpty(overrideLlvm) && !WasmData.Paths.Equals(Path.GetFullPath(overrideLlvm), llvm))
            throw new AutomationException("A separate EM_LLVM_ROOT does not override UE's direct compiler. Use the selected SDK's upstream/bin consistently.");
        return new WasmPreparationTools(RequiredFile(command, "UnrealSharpWasmDotNet"), sdk, config, cache, llvm,
            RequiredFile(command, "UnrealSharpWasmCMake"), RequiredFile(command, "UnrealSharpWasmNinja"));
    }

    private static string Required(BuildCommand command, string name)
    {
        string[] values = command.Params.Where(argument => argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0][(name.Length + 1)..]))
            throw new AutomationException("UnrealSharp Wasm requires one explicit -{0}= path.", name);
        string value = values[0][(name.Length + 1)..].Trim('"');
        if (!Path.IsPathFullyQualified(value)) throw new AutomationException("-{0}= must be an absolute path.", name);
        return Path.GetFullPath(value);
    }
    private static string RequiredFile(BuildCommand command, string name)
    {
        string path = Required(command, name);
        if (!File.Exists(path)) throw new AutomationException("Missing -{0}= file: {1}", name, path);
        return path;
    }
    private static string RequiredDirectory(BuildCommand command, string name)
    {
        string path = Required(command, name);
        if (!Directory.Exists(path)) throw new AutomationException("Missing -{0}= directory: {1}", name, path);
        return path;
    }
    private static string QuoteLinkPath(string path)
    {
        if (path.IndexOfAny(['"', '\r', '\n']) >= 0) throw new AutomationException("Unsupported quote/newline in native archive path.");
        return "\"" + path.Replace('\\', '/') + "\"";
    }
}
