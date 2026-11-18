#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
#if !UNREALSHARP_WASM_DATA_ONLY
using UnrealBuildTool;

public class UnrealSharpWasmRuntime : ModuleRules
{
    public UnrealSharpWasmRuntime(ReadOnlyTargetRules target) : base(target)
    {
        Type = ModuleType.External;
        if (!target.Platform.ToString().Equals("Wasm", StringComparison.Ordinal)) return;
#if UE_5_9_OR_LATER
        var profile = GetBuildProfile(target, PluginDirectory, EngineDirectory);
        UnrealSharp.Wasm.WasmBuildInput input = UnrealSharp.Wasm.WasmBuildInput.Load(profile.InputFile, profile);
        input.RuntimeProduct.RequireUeExceptionAbi();
        foreach (string include in input.RuntimeProduct.HostIncludeDirectories) PublicSystemIncludePaths.Add(include);
        PublicPreBuildLibraries.Add(input.AppArchive);
        // Preserve the consumer-authored archive order and repetitions: ordinary
        // UBT library collections deduplicate. SDK C++ libraries remain SDK-owned.
        PublicSystemLibraries.Add("-Wl,@" + input.LinkResponse.Replace('\\', '/'));
        PublicSystemLibraries.Add("-fexceptions");
        PublicSystemLibraries.Add("-sDISABLE_EXCEPTION_CATCHING=0");
        PublicSystemLibraries.Add("-sSUPPORT_LONGJMP=emscripten");
        foreach (string library in input.RuntimeProduct.JavaScriptLibraries)
            PublicSystemLibraries.Add("--js-library=" + library.Replace('\\', '/'));
        if (!input.RuntimeProduct.IsNativeAot)
        {
            PublicSystemLibraries.Add("--pre-js=" + input.BootstrapFile.Replace('\\', '/'));
            PublicSystemLibraries.Add("--js-library=" + Path.Combine(PluginDirectory, "Build", "Runtime", "Wasm", "unrealsharp-wasm-library.js").Replace('\\', '/'));
        }
        foreach (string path in new[] { input.InputFile, input.RuntimeProductFile, input.LinkResponse, input.BootstrapFile, input.PreparedReadyFile,
            Path.Combine(PluginDirectory, "Build", "Runtime", "Wasm", "InvokeBuildGuard.ps1"),
            Path.Combine(PluginDirectory, "Build", "Runtime", "Wasm", "InvokeBuildGuard.py"),
            Path.Combine(PluginDirectory, "Build", "Runtime", "Wasm", "unrealsharp-wasm-library.js"),
            Path.Combine(PluginDirectory, "Build", "Runtime", "Wasm", "BuildGuard", "Program.cs"),
            Path.Combine(PluginDirectory, "Build", "Runtime", "Wasm", "BuildGuard", "WindowsUbtInvocation.cs"),
            Path.Combine(PluginDirectory, "Build", "Runtime", "Wasm", "BuildGuard", "MacUbtInvocation.cs"),
            Path.Combine(PluginDirectory, "Build", "Runtime", "Wasm", "BuildGuard", "UbtInvocation.cs"),
            Path.Combine(PluginDirectory, "Build", "Runtime", "Wasm", "BuildGuard", "UnrealSharp.Wasm.BuildGuard.csproj") }) ExternalDependencies.Add(path);
        foreach (UnrealSharp.Wasm.WasmProductAsset asset in input.RuntimeProduct.Assets) ExternalDependencies.Add(input.RuntimeProduct.RequireAsset(asset.Path));
#else
        throw new BuildException("UnrealSharp static Wasm hosting requires UE 5.9 or later.");
#endif
    }
#if UE_5_9_OR_LATER
    internal static UnrealSharp.Wasm.WasmBuildProfile GetBuildProfile(ReadOnlyTargetRules target, string pluginDirectory, string engineDirectory)
    {
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) || target.ProjectFile == null)
            throw new BuildException("UnrealSharp static Wasm hosting requires a Windows- or Mac-hosted UE 5.9 code project.");
        string[] architectures = target.Architectures.Architectures.Select(architecture => architecture.ToString()).ToArray();
        // The Wasm platform extension is optional in desktop engine installations.
        object wasmRules = target.GetType().GetProperty("WasmPlatform")?.GetValue(target)
            ?? throw new BuildException("The Wasm target requires the UE Wasm platform extension.");
        bool ReadWasmFlag(string name) =>
            wasmRules.GetType().GetProperty(name)?.GetValue(wasmRules) is bool value
                ? value : throw new BuildException("Missing UE Wasm target rule: {0}", name);
        if (architectures.Length != 1 || architectures[0] != "wasm64" || !ReadWasmFlag("bWasm64"))
            throw new BuildException("UnrealSharp static hosting requires the actual single wasm64 native target.");
        return new UnrealSharp.Wasm.WasmBuildProfile(target.ProjectFile.FullName, pluginDirectory, engineDirectory,
            target.Name, target.Type.ToString(), target.Platform.ToString(), target.Configuration.ToString(), architectures[0],
            ReadWasmFlag("bLowerMemory64"), ReadWasmFlag("bSimd128"));
    }
#endif

}
#endif

namespace UnrealSharp.Wasm
{
    public sealed record WasmBuildProfile(string ProjectFile, string PluginDirectory, string EngineDirectory,
        string TargetName, string TargetType, string Platform, string Configuration, string Architecture,
        bool LowerMemory64, bool Simd128)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public string BuildDirectory => Path.Combine(Path.GetDirectoryName(ProjectFile)!, "Intermediate", "UnrealSharp", "Wasm", "NativeBuild", TargetName, Configuration, Architecture);
        [System.Text.Json.Serialization.JsonIgnore]
        public string InputFile => Path.Combine(BuildDirectory, "BuildInput.json");
        internal void Validate()
        {
            WasmData.Full(ProjectFile); WasmData.Full(PluginDirectory); WasmData.Full(EngineDirectory);
            WasmData.Require(Path.GetExtension(ProjectFile).Equals(".uproject", StringComparison.OrdinalIgnoreCase) && File.Exists(ProjectFile), "Missing actual Wasm project.");
            WasmData.Require(TargetType == "Game" && Platform == "Wasm" && Architecture == "wasm64" && LowerMemory64 && Simd128,
                "The static host currently supports one UE Wasm64 lowered-memory SIMD Game profile.");
            WasmData.Require(TargetName.Length > 0 && TargetName.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') &&
                new[] { "Debug", "DebugGame", "Development", "Shipping", "Test" }.Contains(Configuration), "Invalid Wasm target/configuration.");
        }
        internal static WasmBuildProfile Parse(JsonElement value)
        {
            WasmData.Fields(value, "ProjectFile", "PluginDirectory", "EngineDirectory", "TargetName", "TargetType", "Platform", "Configuration", "Architecture", "LowerMemory64", "Simd128");
            var result = new WasmBuildProfile(WasmData.String(value, "ProjectFile"), WasmData.String(value, "PluginDirectory"), WasmData.String(value, "EngineDirectory"),
                WasmData.String(value, "TargetName"), WasmData.String(value, "TargetType"), WasmData.String(value, "Platform"), WasmData.String(value, "Configuration"),
                WasmData.String(value, "Architecture"), value.GetProperty("LowerMemory64").GetBoolean(), value.GetProperty("Simd128").GetBoolean());
            result.Validate(); return result;
        }
    }

    public sealed record WasmProductAsset(string Path, string Kind, long Bytes, string Sha256);
    public sealed record WasmLinkItem(string Kind, string Value);
    public sealed record WasmPreparedFile(string Path, long Bytes, string Sha256);
    public sealed record WasmUhtIdentity(string ManifestFile, string ManifestHash, string Context, string Inputs, string Transaction);
    public sealed record WasmPreparedPayload(WasmBuildProfile Profile, string NativeProductKey, string RuntimeProductSha256,
        WasmUhtIdentity Uht, string ManagedDirectory, string RuntimeManifestFile, WasmToolchainPaths ToolchainPaths, IReadOnlyList<WasmPreparedFile> Files);
    public sealed record WasmPathLink(string RawTarget, string Target, string PhysicalTarget);
    public sealed record WasmToolchainPaths(IReadOnlyDictionary<string, string> Roots, IReadOnlyDictionary<string, WasmPathLink> Links);

    // This file is compiled by RulesCompiler as a real External module and is
    // Compile-linked into Automation and the guard. No Rules/Automation assembly cycle.
    public sealed class WasmRuntimeProduct
    {
        public string DescriptorFile { get; private init; } = "";
        public string DescriptorSha256 { get; private init; } = "";
        public string RootDirectory => Path.GetDirectoryName(DescriptorFile)!;
        public string NativeProductKey { get; private init; } = "";
        public bool IsNativeAot { get; private init; }
        public string ExceptionModel { get; private init; } = "";
        public JsonElement Product { get; private init; }
        public IReadOnlyList<WasmProductAsset> Assets { get; private init; } = [];
        public IReadOnlyList<string> HostIncludeDirectories { get; private init; } = [];
        public IReadOnlyList<string> HelperIncludeDirectories { get; private init; } = [];
        public string HelperForceInclude { get; private init; } = "";
        public IReadOnlyList<string> Archives { get; private init; } = [];
        public IReadOnlyList<WasmLinkItem> LinkSequence { get; private init; } = [];
        public IReadOnlyList<string> CompileOptions { get; private init; } = [];
        public IReadOnlyList<string> JavaScriptLibraries { get; private init; } = [];
        public string EmittedGitHash { get; private init; } = "";
        public string CoreLib { get; private init; } = "";
        public IReadOnlyList<string> ManagedFiles { get; private init; } = [];
        public string HelperToolEntry { get; private init; } = "";
        public IReadOnlyList<string> HelperToolFiles { get; private init; } = [];
        private IReadOnlyList<WasmPreparedFile> EvidenceFiles { get; init; } = [];

        public static WasmRuntimeProduct OpenPortable(string file)
        {
            file = WasmData.Full(file); WasmData.NoLinks(file);
            byte[] raw = File.ReadAllBytes(file);
            using JsonDocument document = WasmData.Parse(raw);
            JsonElement root = document.RootElement;
            if (WasmData.String(root, "Schema") == "UnrealSharp.WasmNativeAotProduct") return OpenNativeAot(file, raw, root);
            WasmData.Fields(root, "Schema", "SchemaVersion", "Kind", "NativeProductKey", "Product", "Evidence", "ExternalTools");
            WasmData.Require(WasmData.String(root, "Schema") == "UnrealSharp.RuntimeProduct" && root.GetProperty("SchemaVersion").GetInt32() == 1 &&
                WasmData.String(root, "Kind") == "Portable", "Expected a completed Portable RuntimeProduct v1.");
            JsonElement product = root.GetProperty("Product");
            WasmData.Fields(product, "RuntimeFlavor", "TargetOS", "Architecture", "SourceRevision", "Abi", "Native", "Includes", "Managed", "JavaScript", "ConsumerPolicy", "Assets");
            WasmData.Require(WasmData.String(product, "RuntimeFlavor") == "CoreCLR" && WasmData.String(product, "TargetOS") == "browser" &&
                WasmData.String(product, "Architecture") == "wasm64" && WasmData.IsHash(WasmData.String(product, "SourceRevision"), 40), "Unexpected runtime product target.");
            JsonElement abi = product.GetProperty("Abi");
            string exceptionModel = WasmData.String(abi, "ExceptionModel");
            bool javaScriptExceptions = exceptionModel == "emscripten";
            if (javaScriptExceptions)
                WasmData.Fields(abi, "PointerSize", "NativePthreads", "ManagedThreads", "Interpreter", "ExceptionModel", "WasmLegacyExceptions", "ReadyToRun");
            else
                WasmData.Fields(abi, "PointerSize", "NativePthreads", "ManagedThreads", "Interpreter", "ExceptionModel", "WasmLegacyExceptions");
            WasmData.Require(abi.GetProperty("PointerSize").GetInt32() == 8 && abi.GetProperty("NativePthreads").GetBoolean() &&
                !abi.GetProperty("ManagedThreads").GetBoolean() && abi.GetProperty("Interpreter").GetBoolean() &&
                (javaScriptExceptions
                    ? abi.GetProperty("WasmLegacyExceptions").ValueKind == JsonValueKind.Null && !abi.GetProperty("ReadyToRun").GetBoolean()
                    : exceptionModel == "wasm" && abi.GetProperty("WasmLegacyExceptions").GetInt32() == 0), "Unsupported runtime ABI.");
            string key = WasmData.String(root, "NativeProductKey");
            WasmData.Require(WasmData.IsHash(key) && key == WasmData.CanonicalHash(new { Schema = "UnrealSharp.RuntimeProduct", SchemaVersion = 1, Product = product }), "RuntimeProduct key mismatch.");
            string directory = Path.GetDirectoryName(file)!;
            var assets = ReadAssets(product, directory);
            string Asset(string relative)
            {
                relative = WasmData.Relative(relative);
                WasmData.Require(assets.Any(asset => asset.Path == relative), "Undeclared runtime asset: " + relative);
                return Path.GetFullPath(Path.Combine(directory, relative));
            }
            string Include(string relative)
            {
                relative = WasmData.Relative(relative); string path = Path.GetFullPath(Path.Combine(directory, relative));
                WasmData.NoLinks(path); WasmData.Require(Directory.Exists(path) && assets.Any(asset => asset.Path.StartsWith(relative + "/", StringComparison.Ordinal)), "Missing declared include directory.");
                return path;
            }
            JsonElement native = product.GetProperty("Native");
            WasmData.Fields(native, "Archives", "LinkSequence", "CompileOptions", "HelperDefinitions");
            var archives = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonElement archive in native.GetProperty("Archives").EnumerateArray())
            {
                WasmData.Fields(archive, "Id", "Asset");
                WasmData.Require(archives.TryAdd(WasmData.String(archive, "Id"), Asset(WasmData.String(archive, "Asset"))), "Duplicate runtime archive id.");
            }
            WasmData.Require(archives.Count == 16, "Expected the complete 16-archive runtime product.");
            var sequence = new List<WasmLinkItem>();
            foreach (JsonElement item in native.GetProperty("LinkSequence").EnumerateArray())
            {
                string kind = WasmData.String(item, "Kind");
                if (kind == "Archive") { WasmData.Fields(item, "Kind", "Archive"); sequence.Add(new(kind, archives[WasmData.String(item, "Archive")])); }
                else { WasmData.Fields(item, "Kind", "Name"); string name = WasmData.String(item, "Name"); WasmData.Require(kind == "SystemLibrary" && name is "stdc++" or "m", "Unsupported SDK system-library role."); sequence.Add(new(kind, name)); }
            }
            WasmData.Require(sequence.Count == 19 && sequence.Count(item => item.Kind == "Archive") == 17 &&
                sequence.Where(item => item.Kind == "Archive").Select(item => item.Value).Distinct(StringComparer.Ordinal).Count() == 16 &&
                sequence.Where(item => item.Kind == "SystemLibrary").Select(item => item.Value).SequenceEqual(new[] { "stdc++", "m" }), "Incomplete ordered native link interface.");
            string[] compileOptions = WasmData.Strings(native.GetProperty("CompileOptions"));
            string[] expectedOptions = javaScriptExceptions
                ? ["-m64", "-pthread", "-fexceptions", "-msimd128"]
                : ["-m64", "-pthread", "-fwasm-exceptions", "-sWASM_LEGACY_EXCEPTIONS=0", "-msimd128"];
            WasmData.Require(compileOptions.SequenceEqual(expectedOptions), "Unexpected runtime ABI compile requirements.");
            string[] definitions = WasmData.Strings(native.GetProperty("HelperDefinitions")).ToArray();
            string[] wasm64Definitions = ["GEN_PINVOKE=1", "TARGET_WASM64", "TARGET_64BIT", "TARGET_POINTER_SIZE=8"];
            string[] legacyDefinitions = ["GEN_PINVOKE=1", "TARGET_WASM", "TARGET_WASM64", "TARGET_64BIT", "TARGET_POINTER_SIZE=8"];
            WasmData.Require(definitions.SequenceEqual(wasm64Definitions) || definitions.SequenceEqual(legacyDefinitions), "Unexpected helper definitions.");
            JsonElement includes = product.GetProperty("Includes"); WasmData.Fields(includes, "Host", "Helper", "ForceInclude", "Inventory");
            foreach (string include in WasmData.Strings(includes.GetProperty("Inventory"))) Asset(include);
            JsonElement managed = product.GetProperty("Managed"); WasmData.Fields(managed, "CoreLib", "CoreLibSha256", "FrameworkDirectory", "Framework", "HelperTool");
            string coreLib = Asset(WasmData.String(managed, "CoreLib"));
            WasmData.Require(WasmData.HashFile(coreLib) == WasmData.String(managed, "CoreLibSha256"), "CoreLib hash mismatch.");
            Include(WasmData.String(managed, "FrameworkDirectory"));
            var framework = new List<string>();
            foreach (JsonElement assembly in managed.GetProperty("Framework").EnumerateArray())
            {
                WasmData.Fields(assembly, "Name", "Asset", "Mvid"); string path = Asset(WasmData.String(assembly, "Asset"));
                WasmData.Require(Path.GetFileNameWithoutExtension(path) == WasmData.String(assembly, "Name") && Guid.TryParse(WasmData.String(assembly, "Mvid"), out _), "Invalid framework identity."); framework.Add(path);
            }
            WasmData.Require(framework.Count == 180 && framework.Distinct(WasmData.Paths).Count() == framework.Count, "Incomplete matching framework.");
            JsonElement tool = managed.GetProperty("HelperTool"); WasmData.Fields(tool, "Entry", "Files", "TargetFramework", "TargetArchitecture", "PointerSize", "Arguments");
            WasmData.Require(WasmData.String(tool, "TargetFramework") == "net12.0" && WasmData.String(tool, "TargetArchitecture") == "Wasm64" &&
                tool.GetProperty("PointerSize").GetInt32() == 8 && WasmData.Strings(tool.GetProperty("Arguments")).SequenceEqual(new[] { "{InputManifest}", "{OutputDirectory}" }), "Unsupported helper CLI contract.");
            JsonElement js = product.GetProperty("JavaScript"); WasmData.Fields(js, "Libraries", "EmittedGitHash", "Bootstrap");
            string[] libraries = WasmData.Strings(js.GetProperty("Libraries")).Select(Asset).ToArray();
            WasmData.Require(libraries.Length == 2 && libraries.Distinct(WasmData.Paths).Count() == 2 && WasmData.IsHash(WasmData.String(js, "EmittedGitHash"), 40) &&
                WasmData.String(js, "Bootstrap") == "TwoSlotNativeBrowserV1", "Unsupported production Browser/Utils bootstrap.");
            JsonElement policy = product.GetProperty("ConsumerPolicy"); WasmData.Fields(policy, "StandardLibraries", "EmscriptenVersion", "HostDefaultsAreExternal");
            WasmData.Require(WasmData.String(policy, "StandardLibraries") == "SdkOwned" && WasmData.String(policy, "EmscriptenVersion") == "6.0.10" && policy.GetProperty("HostDefaultsAreExternal").GetBoolean(), "Unsupported native consumer policy.");
            var evidence = new List<WasmPreparedFile>();
            foreach (JsonElement item in root.GetProperty("Evidence").EnumerateArray())
            {
                WasmData.Fields(item, "Kind", "OriginalPath", "Path", "Bytes", "Sha256", "ExitCode", "Scope");
                string path = Path.GetFullPath(Path.Combine(directory, WasmData.Relative(WasmData.String(item, "Path"))));
                var entry = new WasmPreparedFile(path, item.GetProperty("Bytes").GetInt64(), WasmData.String(item, "Sha256"));
                string evidenceKind = WasmData.String(item, "Kind");
                WasmData.Require((evidenceKind == "Metadata" ? item.GetProperty("ExitCode").ValueKind == JsonValueKind.Null :
                    evidenceKind is "Build" or "Node" or "Chrome" or "NativeGraph" && item.GetProperty("ExitCode").GetInt32() == 0) &&
                    WasmData.String(item, "Scope").Length > 0, "Unsuccessful product evidence.");
                WasmData.VerifyFile(path, entry.Bytes, entry.Sha256, noLinks: true); evidence.Add(entry);
            }
            WasmData.Require(evidence.Count > 0, "Missing runtime product evidence.");
            if (javaScriptExceptions)
            {
                const string capabilitiesFile = "evidence/runtime-capabilities.json";
                WasmData.Require(root.GetProperty("Evidence").EnumerateArray().Any(item =>
                    WasmData.String(item, "Path") == capabilitiesFile && WasmData.String(item, "Kind") == "Metadata" &&
                    item.GetProperty("ExitCode").ValueKind == JsonValueKind.Null), "Missing emitted JS-EH runtime capabilities.");
                using JsonDocument capabilities = WasmData.Parse(File.ReadAllBytes(Path.Combine(directory, capabilitiesFile)));
                WasmData.Fields(capabilities.RootElement, "Properties");
                JsonElement properties = capabilities.RootElement.GetProperty("Properties");
                var expected = new Dictionary<string, string> { ["RuntimeWasmNativePthreads"] = "true", ["RuntimeCoreClrBrowserJsEh"] = "true",
                    ["RuntimeWasmExceptionModel"] = "emscripten", ["RuntimeWasmAssemblyFormat"] = "raw-pe", ["RuntimeWasmInterpreterOnly"] = "true",
                    ["RuntimeWasmReadyToRun"] = "false", ["RuntimeWasmWebcil"] = "false" };
                WasmData.Fields(properties, expected.Keys.ToArray());
                WasmData.Require(expected.All(entry => WasmData.String(properties, entry.Key) == entry.Value), "Emitted JS-EH runtime capabilities do not match the selected ABI.");
            }
            foreach (JsonElement item in root.GetProperty("ExternalTools").EnumerateArray())
            {
                WasmData.Fields(item, "Role", "Path", "Sha256", "Version");
                WasmData.Require(WasmData.IsHash(WasmData.String(item, "Sha256")), "Invalid external tool provenance.");
            }
            var result = new WasmRuntimeProduct { DescriptorFile = file, DescriptorSha256 = WasmData.Hash(raw), NativeProductKey = key, Product = product.Clone(), Assets = assets.AsReadOnly(),
                ExceptionModel = exceptionModel, Archives = archives.Values.ToArray(), LinkSequence = sequence.AsReadOnly(), CompileOptions = compileOptions,
                HostIncludeDirectories = WasmData.Strings(includes.GetProperty("Host")).Select(Include).ToArray(), HelperIncludeDirectories = WasmData.Strings(includes.GetProperty("Helper")).Select(Include).ToArray(),
                HelperForceInclude = Asset(WasmData.String(includes, "ForceInclude")), JavaScriptLibraries = libraries, EmittedGitHash = WasmData.String(js, "EmittedGitHash"),
                CoreLib = coreLib, ManagedFiles = framework.AsReadOnly(), HelperToolEntry = Asset(WasmData.String(tool, "Entry")),
                HelperToolFiles = WasmData.Strings(tool.GetProperty("Files")).Select(Asset).ToArray(), EvidenceFiles = evidence.AsReadOnly() };
            result.VerifyInventory(); return result;
        }

        private static List<WasmProductAsset> ReadAssets(JsonElement product, string directory)
        {
            var assets = new List<WasmProductAsset>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement item in product.GetProperty("Assets").EnumerateArray())
            {
                WasmData.Fields(item, "Path", "Kind", "Bytes", "Sha256");
                string relative = WasmData.Relative(WasmData.String(item, "Path"));
                var asset = new WasmProductAsset(relative, WasmData.String(item, "Kind"), item.GetProperty("Bytes").GetInt64(), WasmData.String(item, "Sha256"));
                WasmData.Require(names.Add(relative) && asset.Kind.Length > 0 && asset.Bytes >= 0 && WasmData.IsHash(asset.Sha256), "Duplicate or invalid runtime asset.");
                WasmData.VerifyFile(Path.Combine(directory, relative), asset.Bytes, asset.Sha256, noLinks: true); assets.Add(asset);
            }
            return assets;
        }

        private static WasmRuntimeProduct OpenNativeAot(string file, byte[] raw, JsonElement root)
        {
            WasmData.Fields(root, "Schema", "SchemaVersion", "Product", "NativeProductKey");
            JsonElement product = root.GetProperty("Product");
            WasmData.Fields(product, "RuntimeFlavor", "TargetOS", "Architecture", "SourceRevision", "Abi", "Compiler", "LinkSequence", "JavaScript", "Assets");
            WasmData.Require(root.GetProperty("SchemaVersion").GetInt32() == 1 && WasmData.String(product, "RuntimeFlavor") == "NativeAot" &&
                WasmData.String(product, "TargetOS") == "browser" && WasmData.String(product, "Architecture") == "wasm64" &&
                WasmData.IsHash(WasmData.String(product, "SourceRevision"), 40), "Unexpected Wasm NativeAOT product.");
            JsonElement abi = product.GetProperty("Abi");
            WasmData.Fields(abi, "PointerSize", "NativePthreads", "ManagedThreads", "LowerMemory64", "NativeExceptions", "ManagedExceptions");
            WasmData.Require(abi.GetProperty("PointerSize").GetInt32() == 8 && abi.GetProperty("NativePthreads").GetBoolean() &&
                !abi.GetProperty("ManagedThreads").GetBoolean() && abi.GetProperty("LowerMemory64").GetBoolean() &&
                WasmData.String(abi, "NativeExceptions") == "emscripten" && WasmData.String(abi, "ManagedExceptions") == "emulated", "Incompatible NativeAOT ABI.");
            string key = WasmData.String(root, "NativeProductKey");
            WasmData.Require(key == WasmData.CanonicalHash(new { Schema = "UnrealSharp.WasmNativeAotProduct", SchemaVersion = 1, Product = product }), "NativeAOT product key mismatch.");
            string directory = Path.GetDirectoryName(file)!;
            var assets = ReadAssets(product, directory);
            string Asset(string relative, string kind)
            {
                WasmData.Require(assets.Any(item => item.Path == relative && item.Kind == kind), "Missing NativeAOT " + kind + " input: " + relative);
                return Path.Combine(directory, relative);
            }
            var sequence = WasmData.Strings(product.GetProperty("LinkSequence")).Select(path => new WasmLinkItem("Archive", Asset(path, "native"))).ToArray();
            WasmData.Require(sequence.Length > 0 && sequence.Select(item => item.Value).ToHashSet(WasmData.Paths)
                .SetEquals(assets.Where(item => item.Kind == "native").Select(item => Asset(item.Path, "native"))), "Incomplete NativeAOT link inputs.");
            var result = new WasmRuntimeProduct { IsNativeAot = true, DescriptorFile = file, DescriptorSha256 = WasmData.Hash(raw),
                NativeProductKey = key, Product = product.Clone(), Assets = assets.AsReadOnly(), ExceptionModel = "emscripten", LinkSequence = sequence,
                CoreLib = Asset("managed/System.Private.CoreLib.dll", "managed"), HelperToolEntry = Asset(WasmData.String(product, "Compiler"), "tools"),
                ManagedFiles = assets.Where(item => item.Kind == "managed").Select(item => Asset(item.Path, "managed")).ToArray(),
                JavaScriptLibraries = new[] { Asset(WasmData.String(product, "JavaScript"), "js") } };
            result.VerifyInventory();
            return result;
        }

        public string RequireAsset(string relative) { relative = WasmData.Relative(relative); WasmData.Require(Assets.Any(asset => asset.Path == relative), "Undeclared product asset."); return Path.GetFullPath(Path.Combine(RootDirectory, relative)); }
        public void RequireUeExceptionAbi()
        {
            WasmData.Require(ExceptionModel == "emscripten",
                "UE's precompiled Wasm objects use Emscripten JavaScript SjLj. This native-Wasm-EH runtime product cannot be linked with them. Select a verified CoreClrBrowserJsEh interpreter-only runtime product; link-only exception flags cannot convert the precompiled objects.");
        }
        public void RequireUnchanged()
        {
            WasmData.NoLinks(DescriptorFile); WasmData.Require(WasmData.HashFile(DescriptorFile) == DescriptorSha256, "RuntimeProduct descriptor changed.");
            foreach (WasmProductAsset asset in Assets) WasmData.VerifyFile(RequireAsset(asset.Path), asset.Bytes, asset.Sha256, noLinks: true);
            foreach (WasmPreparedFile file in EvidenceFiles) WasmData.VerifyFile(file.Path, file.Bytes, file.Sha256, noLinks: true);
            VerifyInventory();
        }
        private void VerifyInventory()
        {
            var expected = new HashSet<string>(WasmData.Paths) { DescriptorFile };
            foreach (WasmProductAsset asset in Assets) WasmData.Require(expected.Add(RequireAsset(asset.Path)), "Duplicate product asset/evidence path.");
            foreach (WasmPreparedFile evidence in EvidenceFiles) WasmData.Require(expected.Add(evidence.Path), "Duplicate product evidence path.");
            IEnumerable<string> Files(string directory)
            {
                WasmData.NoLinks(directory);
                foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    WasmData.NoLinks(path);
                    if (Directory.Exists(path)) foreach (string child in Files(path)) yield return child;
                    else yield return Path.GetFullPath(path);
                }
            }
            WasmData.Require(expected.SetEquals(Files(RootDirectory)), "RuntimeProduct has missing/undeclared files.");
        }
    }

    public sealed class WasmBuildInput
    {
        public WasmBuildProfile Profile { get; private init; } = null!;
        public WasmRuntimeProduct RuntimeProduct { get; private init; } = null!;
        public string InputFile => Profile.InputFile;
        public string InputSha256 { get; private set; } = "";
        public string RuntimeProductFile => RuntimeProduct.DescriptorFile;
        public string RuntimeProductSha256 => RuntimeProduct.DescriptorSha256;
        public string NativeProductKey => RuntimeProduct.NativeProductKey;
        public string ProductDirectory => Path.Combine(Profile.BuildDirectory, NativeProductKey);
        public string AppArchive => Path.Combine(ProductDirectory, "UnrealSharp.Wasm.a");
        public string LinkResponse => Path.Combine(ProductDirectory, "runtime-link.rsp");
        public string BootstrapFile => Path.Combine(ProductDirectory, "wasm-bootstrap.js");
        public string PreparedReadyFile => Path.Combine(ProductDirectory, "AppReady.json");
        public string PreparationLockFile => Path.Combine(Profile.BuildDirectory, "UnrealSharp.Preparation.lock");
        internal bool HasPreparationLease { get; private set; }
        internal string PhysicalBuildDirectory => _paths.Roots[Profile.BuildDirectory];
        private WasmFrozenPaths _paths = null!;

        public static WasmBuildInput Create(WasmBuildProfile profile, WasmRuntimeProduct product)
        {
            profile.Validate(); product.RequireUnchanged();
            return new WasmBuildInput { Profile = profile, RuntimeProduct = product, _paths = WasmFrozenPaths.Capture(profile) };
        }
        public static WasmBuildInput Load(string file, WasmBuildProfile expectedProfile)
        {
            expectedProfile.Validate();
            WasmData.Require(WasmData.Paths.Equals(WasmData.Full(file), expectedProfile.InputFile), "Unexpected Wasm BuildInput location.");
            WasmFrozenPaths paths = WasmFrozenPaths.Capture(expectedProfile); paths.Ensure(file);
            byte[] raw = File.ReadAllBytes(file); using JsonDocument document = WasmData.Parse(raw); JsonElement root = document.RootElement;
            WasmData.Fields(root, "Schema", "SchemaVersion", "Profile", "RuntimeProductFile", "RuntimeProductSha256", "NativeProductKey", "AppArchive", "LinkResponse", "BootstrapFile", "PreparedReadyFile", "PathIdentities", "AncestorLinks");
            WasmData.Require(WasmData.String(root, "Schema") == "UnrealSharp.WasmBuildInput" && root.GetProperty("SchemaVersion").GetInt32() == 1 &&
                WasmBuildProfile.Parse(root.GetProperty("Profile")) == expectedProfile, "Wasm BuildInput profile mismatch.");
            paths.Matches(root.GetProperty("PathIdentities"), root.GetProperty("AncestorLinks"));
            WasmRuntimeProduct product = WasmRuntimeProduct.OpenPortable(WasmData.String(root, "RuntimeProductFile"));
            WasmData.Require(product.DescriptorSha256 == WasmData.String(root, "RuntimeProductSha256") && product.NativeProductKey == WasmData.String(root, "NativeProductKey"), "Wasm runtime selection changed.");
            var input = new WasmBuildInput { Profile = expectedProfile, RuntimeProduct = product, InputSha256 = WasmData.Hash(raw), _paths = paths };
            foreach (var entry in new Dictionary<string, string> { ["AppArchive"] = input.AppArchive, ["LinkResponse"] = input.LinkResponse, ["BootstrapFile"] = input.BootstrapFile, ["PreparedReadyFile"] = input.PreparedReadyFile })
                WasmData.Require(WasmData.Paths.Equals(WasmData.Full(WasmData.String(root, entry.Key)), entry.Value), "Unexpected Wasm prepared path: " + entry.Key);
            input.RequireUnchanged(); return input;
        }
        public void WriteAtomic()
        {
            RuntimeProduct.RequireUnchanged(); _paths.Verify();
            using IDisposable lease = OpenLease(write: true, create: true);
            var value = new { Schema = "UnrealSharp.WasmBuildInput", SchemaVersion = 1, Profile, RuntimeProductFile, RuntimeProductSha256, NativeProductKey,
                AppArchive, LinkResponse, BootstrapFile, PreparedReadyFile, PathIdentities = _paths.Roots, AncestorLinks = _paths.Links };
            byte[] raw = WasmData.CanonicalBytes(JsonSerializer.SerializeToElement(value));
            _paths.WriteAtomic(InputFile, raw); InputSha256 = WasmData.Hash(raw); RequireUnchanged();
        }
        public void RequireUnchanged()
        {
            _paths.Verify(); RuntimeProduct.RequireUnchanged();
            WasmData.Require(InputSha256.Length > 0, "BuildInput has not been written.");
            _paths.Ensure(InputFile); WasmData.Require(WasmData.HashFile(InputFile) == InputSha256, "Wasm BuildInput changed."); _paths.Verify();
        }
        public void RequireSafePath(string path) => _paths.Ensure(WasmData.Full(path));
        public IDisposable AcquirePreparationLock() { RequireUnchanged(); return OpenLease(write: true, create: false); }
        public IDisposable AcquireBuildLease() { RequireUnchanged(); return OpenLease(write: false, create: false); }
        private IDisposable OpenLease(bool write, bool create)
        {
            WasmData.Require(!HasPreparationLease, "This input already owns its preparation lock.");
            _paths.Verify(); _paths.Ensure(PreparationLockFile);
            if (create) { Directory.CreateDirectory(Profile.BuildDirectory); _paths.Ensure(PreparationLockFile); }
            FileStream stream = new(PreparationLockFile, create ? FileMode.OpenOrCreate : FileMode.Open, FileAccess.Read, write ? FileShare.None : FileShare.Read);
            HasPreparationLease = write;
            try { _paths.Verify(); } catch { stream.Dispose(); HasPreparationLease = false; throw; }
            return new WasmLease(() => { stream.Dispose(); HasPreparationLease = false; });
        }
        internal void WriteFileAtomic(string file, byte[] bytes) => _paths.WriteAtomic(file, bytes);
    }

    public sealed class WasmPreparedApp
    {
        public WasmBuildInput Input { get; private init; } = null!;
        public WasmPreparedPayload Payload { get; private init; } = null!;
        public string ReadyKey { get; private init; } = "";
        public string ReadyFile => Input.PreparedReadyFile;
        public string ReadySha256 { get; private init; } = "";
        public string ManagedDirectory => Payload.ManagedDirectory;
        public static WasmPreparedApp Load(WasmBuildInput input)
        {
            input.RequireUnchanged(); input.RequireSafePath(input.PreparedReadyFile);
            byte[] raw = File.ReadAllBytes(input.PreparedReadyFile); using JsonDocument document = WasmData.Parse(raw); JsonElement root = document.RootElement;
            WasmData.Fields(root, "Schema", "SchemaVersion", "ReadyKey", "Payload");
            WasmData.Require(WasmData.String(root, "Schema") == "UnrealSharp.WasmPreparedApp" && root.GetProperty("SchemaVersion").GetInt32() == 2, "Unsupported prepared app.");
            JsonElement value = root.GetProperty("Payload");
            WasmData.Fields(value, "Profile", "NativeProductKey", "RuntimeProductSha256", "Uht", "ManagedDirectory", "RuntimeManifestFile", "ToolchainPaths", "Files");
            JsonElement uht = value.GetProperty("Uht"); WasmData.Fields(uht, "ManifestFile", "ManifestHash", "Context", "Inputs", "Transaction");
            var files = new List<WasmPreparedFile>();
            foreach (JsonElement item in value.GetProperty("Files").EnumerateArray())
            {
                WasmData.Fields(item, "Path", "Bytes", "Sha256"); files.Add(new(WasmData.String(item, "Path"), item.GetProperty("Bytes").GetInt64(), WasmData.String(item, "Sha256")));
            }
            var payload = new WasmPreparedPayload(WasmBuildProfile.Parse(value.GetProperty("Profile")), WasmData.String(value, "NativeProductKey"), WasmData.String(value, "RuntimeProductSha256"),
                new(WasmData.String(uht, "ManifestFile"), WasmData.String(uht, "ManifestHash"), WasmData.String(uht, "Context"), WasmData.String(uht, "Inputs"), WasmData.String(uht, "Transaction")),
                WasmData.String(value, "ManagedDirectory"), WasmData.String(value, "RuntimeManifestFile"), WasmFrozenPaths.ReadToolchain(value.GetProperty("ToolchainPaths")), files.AsReadOnly());
            var result = new WasmPreparedApp { Input = input, Payload = payload, ReadyKey = WasmData.String(root, "ReadyKey"), ReadySha256 = WasmData.Hash(raw) };
            WasmData.Require(WasmData.IsHash(result.ReadyKey) && result.ReadyKey == WasmData.Hash(WasmData.CanonicalBytes(value)), "Prepared app key mismatch.");
            result.RequireUnchanged(); return result;
        }
        public static WasmPreparedApp WriteAtomic(WasmBuildInput input, WasmPreparedPayload payload)
        {
            using IDisposable? lease = input.HasPreparationLease ? null : input.AcquirePreparationLock();
            Validate(input, payload); input.RequireUnchanged();
            string key = WasmData.CanonicalHash(payload);
            byte[] raw = WasmData.CanonicalBytes(JsonSerializer.SerializeToElement(new { Schema = "UnrealSharp.WasmPreparedApp", SchemaVersion = 2, ReadyKey = key, Payload = payload }));
            input.WriteFileAtomic(input.PreparedReadyFile, raw); return Load(input);
        }
        private static void Validate(WasmBuildInput input, WasmPreparedPayload payload)
        {
            WasmData.Require(payload.Profile == input.Profile && payload.NativeProductKey == input.NativeProductKey && payload.RuntimeProductSha256 == input.RuntimeProductSha256, "Prepared app selection/profile mismatch.");
            WasmData.Require(WasmData.IsDigest(payload.Uht.ManifestHash) && WasmData.IsDigest(payload.Uht.Context) && WasmData.IsDigest(payload.Uht.Inputs) && WasmData.IsDigest(payload.Uht.Transaction), "Invalid UHT producer identity.");
            string manifest = Path.Combine(Path.GetDirectoryName(input.Profile.ProjectFile)!, "Intermediate", "Build", "Wasm", input.Profile.TargetName, input.Profile.TargetName + ".uhtmanifest");
            WasmData.Require(WasmData.Paths.Equals(WasmData.Full(payload.Uht.ManifestFile), manifest), "Unexpected prepared UHT manifest.");
            string managed = WasmData.Full(payload.ManagedDirectory);
            WasmData.Require(WasmData.Under(managed, input.ProductDirectory) && WasmData.Paths.Equals(WasmData.Full(payload.RuntimeManifestFile), Path.Combine(managed, "UnrealSharp.Runtime.json")), "Prepared managed directory/manifest is outside this product.");
            WasmFrozenPaths sources = WasmFrozenPaths.FromToolchain(payload.ToolchainPaths);
            foreach (var source in sources.Roots)
                foreach (string path in new[] { source.Key, source.Value })
                    WasmData.Require(!WasmData.Under(path, input.Profile.BuildDirectory) && !WasmData.Under(path, input.PhysicalBuildDirectory) && !WasmData.Under(path, input.RuntimeProduct.RootDirectory), "Toolchain source paths cannot authorize prepared outputs or Portable runtime files.");
            var seen = new HashSet<string>(WasmData.Paths);
            foreach (WasmPreparedFile file in payload.Files)
            {
                string path = WasmData.Full(file.Path); WasmData.Require(seen.Add(path) && !WasmData.Paths.Equals(path, input.PreparedReadyFile), "Duplicate/self-hashed Ready input.");
                sources.EnsureRead(input, path); WasmData.VerifyFile(path, file.Bytes, file.Sha256); sources.EnsureRead(input, path);
            }
            foreach (string required in new[] { input.InputFile, input.AppArchive, input.LinkResponse, input.BootstrapFile, payload.Uht.ManifestFile, payload.RuntimeManifestFile })
                WasmData.Require(seen.Contains(required), "Prepared closure omits required input: " + required);
            WasmPreparedFile selection = payload.Files.Single(file => WasmData.Paths.Equals(file.Path, input.InputFile));
            WasmData.Require(selection.Sha256 == input.InputSha256, "Ready does not bind raw BuildInput bytes.");
            string[] actualManaged = Directory.EnumerateFiles(managed, "*.dll", SearchOption.TopDirectoryOnly).Select(WasmData.Full).ToArray();
            if (input.RuntimeProduct.IsNativeAot)
                WasmData.Require(actualManaged.Length == 0, "A prelinked game may stage only its manifests.");
            else WasmData.Require(actualManaged.Length > 0 && actualManaged.All(seen.Contains) && actualManaged.Any(path => Path.GetFileName(path) == "System.Private.CoreLib.dll") &&
                actualManaged.Any(path => Path.GetFileName(path) == "UnrealSharp.Plugins.dll"), "Prepared loose PE closure is incomplete.");
            sources.Verify();
        }
        public void RequireUnchanged()
        {
            Input.RequireUnchanged(); Input.RequireSafePath(ReadyFile);
            WasmData.Require(WasmData.HashFile(ReadyFile) == ReadySha256, "Prepared app record changed.");
            Validate(Input, Payload); Input.RequireUnchanged();
        }
    }

    internal sealed class WasmLease : IDisposable
    {
        private Action? _dispose;
        internal WasmLease(Action dispose) { _dispose = dispose; }
        public void Dispose() { Action? action = System.Threading.Interlocked.Exchange(ref _dispose, null); action?.Invoke(); }
    }

    internal sealed class WasmFrozenPaths
    {
        private string BuildDirectory { get; init; } = "";
        private bool ToolchainSources { get; init; }
        public SortedDictionary<string, string> Roots { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, WasmPathLink> Links { get; } = new(WasmData.Paths);
        // Source snapshots never own a write directory. Each observed logical
        // path binds all its ancestor and final links, including chained targets.
        internal static WasmFrozenPaths CreateToolchain() => new() { ToolchainSources = true };
        internal string CaptureSource(string path)
        {
            path = WasmData.Full(path);
            string physical = Resolve(path, capture: true, allowFinal: true);
            WasmData.Require(File.Exists(physical) || Directory.Exists(physical), "Missing/dangling toolchain source: " + path);
            WasmData.Require(!Roots.TryGetValue(path, out string? previous) || WasmData.Paths.Equals(previous, physical), "Toolchain source identity changed: " + path);
            Roots[path] = physical; return physical;
        }
        internal WasmToolchainPaths SnapshotToolchain()
        {
            Verify();
            return new(new SortedDictionary<string, string>(Roots, StringComparer.Ordinal), new SortedDictionary<string, WasmPathLink>(Links, WasmData.Paths));
        }
        internal static WasmFrozenPaths FromToolchain(WasmToolchainPaths snapshot)
        {
            var result = CreateToolchain();
            foreach (var root in snapshot.Roots) result.Roots.Add(WasmData.Full(root.Key), WasmData.Full(root.Value));
            foreach (var link in snapshot.Links)
            {
                WasmData.Require(!string.IsNullOrEmpty(link.Value.RawTarget), "Empty toolchain link target.");
                result.Links.Add(WasmData.Full(link.Key), new(link.Value.RawTarget, WasmData.Full(link.Value.Target), WasmData.Full(link.Value.PhysicalTarget)));
            }
            result.Verify(); return result;
        }
        internal static WasmToolchainPaths ReadToolchain(JsonElement value)
        {
            WasmData.Fields(value, "Roots", "Links");
            var roots = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty root in value.GetProperty("Roots").EnumerateObject()) roots.Add(root.Name, root.Value.GetString() ?? throw new InvalidDataException("Null toolchain source root."));
            var links = new SortedDictionary<string, WasmPathLink>(WasmData.Paths);
            foreach (JsonProperty link in value.GetProperty("Links").EnumerateObject())
            {
                WasmData.Fields(link.Value, "RawTarget", "Target", "PhysicalTarget");
                links.Add(link.Name, new(WasmData.String(link.Value, "RawTarget"), WasmData.String(link.Value, "Target"), WasmData.String(link.Value, "PhysicalTarget")));
            }
            return new(roots, links);
        }
        internal void EnsureRead(WasmBuildInput input, string path)
        {
            path = WasmData.Full(path);
            if (Roots.TryGetValue(path, out string? physical))
                WasmData.Require(WasmData.Paths.Equals(Resolve(path), physical) && (File.Exists(physical) || Directory.Exists(physical)), "Toolchain source identity changed: " + path);
            else input.RequireSafePath(path);
        }
        internal static WasmFrozenPaths Capture(WasmBuildProfile profile)
        {
            var result = new WasmFrozenPaths { BuildDirectory = profile.BuildDirectory };
            foreach (string path in new[] { Path.GetDirectoryName(profile.ProjectFile)!, profile.PluginDirectory, profile.EngineDirectory, profile.BuildDirectory }.Distinct(WasmData.Paths))
                result.Roots.Add(path, result.Resolve(path, capture: true));
            result.Verify(); return result;
        }
        internal void Matches(JsonElement roots, JsonElement links)
        {
            WasmData.Require(roots.ValueKind == JsonValueKind.Object && roots.EnumerateObject().Count() == Roots.Count &&
                roots.EnumerateObject().All(entry => Roots.TryGetValue(entry.Name, out string? physical) && WasmData.Paths.Equals(physical, entry.Value.GetString())), "BuildInput physical roots changed.");
            WasmData.Require(links.ValueKind == JsonValueKind.Object && links.EnumerateObject().Count() == Links.Count, "BuildInput ancestor chain changed.");
            foreach (JsonProperty entry in links.EnumerateObject())
            {
                WasmData.Fields(entry.Value, "RawTarget", "Target", "PhysicalTarget");
                WasmData.Require(Links.TryGetValue(entry.Name, out WasmPathLink? actual) && actual.RawTarget == WasmData.String(entry.Value, "RawTarget") &&
                    WasmData.Paths.Equals(actual.Target, WasmData.String(entry.Value, "Target")) && WasmData.Paths.Equals(actual.PhysicalTarget, WasmData.String(entry.Value, "PhysicalTarget")), "BuildInput ancestor target changed.");
            }
        }
        internal void Ensure(string path)
        {
            path = WasmData.Full(path);
            if (!Roots.Keys.Any(root => WasmData.Under(path, root))) { WasmData.NoLinks(path); return; }
            var prefixes = new Dictionary<string, string>(WasmData.Paths); Resolve(path, prefixes: prefixes);
            foreach (var root in Roots.Where(root => WasmData.Under(path, root.Key)))
                WasmData.Require(prefixes.TryGetValue(root.Key, out string? physical) && WasmData.Paths.Equals(physical, root.Value), "Wasm input physical root changed: " + root.Key);
        }
        internal void Verify()
        {
            foreach (var root in Roots) WasmData.Require(WasmData.Paths.Equals(Resolve(root.Key), root.Value) &&
                (!ToolchainSources || File.Exists(root.Value) || Directory.Exists(root.Value)), "Wasm root identity changed.");
            foreach (var link in Links)
            {
                var info = new FileInfo(link.Key);
                WasmData.Require(info.LinkTarget == link.Value.RawTarget && WasmData.Paths.Equals(Resolve(link.Key), link.Value.PhysicalTarget), "Frozen Wasm path link changed: " + link.Key);
            }
        }
        internal void WriteAtomic(string file, byte[] raw)
        {
            WasmData.Require(BuildDirectory.Length > 0 && WasmData.Under(WasmData.Full(file), BuildDirectory), "Wasm write is outside the current native-build root.");
            Verify(); Ensure(file); Directory.CreateDirectory(Path.GetDirectoryName(file)!); Ensure(file);
            if (File.Exists(file) && File.ReadAllBytes(file).SequenceEqual(raw)) return;
            string temporary = file + ".tmp." + Guid.NewGuid().ToString("N");
            try { Ensure(temporary); File.WriteAllBytes(temporary, raw); Ensure(file); File.Move(temporary, file, overwrite: true); }
            finally { Ensure(temporary); if (File.Exists(temporary)) File.Delete(temporary); }
            Verify();
        }
        private string Resolve(string path, bool capture = false, bool allowFinal = false, HashSet<string>? chain = null, Dictionary<string, string>? prefixes = null)
        {
            string full = WasmData.Full(path), current = Path.GetPathRoot(full)!, logical = current;
            string[] parts = full[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            chain ??= new HashSet<string>(WasmData.Paths);
            for (int index = 0; index < parts.Length; index++)
            {
                current = Path.Combine(current, parts[index]); logical = Path.Combine(logical, parts[index]); FileAttributes attributes;
                try { attributes = File.GetAttributes(current); } catch (FileNotFoundException) { attributes = 0; } catch (DirectoryNotFoundException) { attributes = 0; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    var info = new DirectoryInfo(current); string raw = info.LinkTarget ?? throw new InvalidDataException("Unsupported Wasm path link.");
                    // ResolveLinkTarget(...).FullName folds '..' lexically. A
                    // preceding directory could itself be a link, so that fold
                    // would omit part of the actual filesystem traversal.
                    bool enteredTarget = false;
                    foreach (string part in raw.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part == "..") WasmData.Require(!enteredTarget, "Non-prefix parent traversal in Wasm link target: " + current);
                        else if (part != ".") enteredTarget = true;
                    }
                    string target = info.ResolveLinkTarget(false)?.FullName ?? throw new InvalidDataException("Unresolved Wasm path link.");
                    bool mayCapture = capture && (allowFinal || index < parts.Length - 1); Links.TryGetValue(current, out WasmPathLink? expected);
                    WasmData.Require(mayCapture || expected != null, "Unverified link in Wasm input: " + current); WasmData.Require(chain.Add(current), "Cyclic Wasm path link.");
                    string physical; try { physical = Resolve(target, mayCapture, true, chain); } finally { chain.Remove(current); }
                    WasmData.Require(expected == null || expected.RawTarget == raw && WasmData.Paths.Equals(expected.Target, target) && WasmData.Paths.Equals(expected.PhysicalTarget, physical), "Wasm path link retargeted.");
                    Links.TryAdd(current, new(raw, target, physical)); current = physical;
                }
                if (prefixes != null) prefixes[logical] = current;
            }
            return current;
        }
    }

    public static class WasmData
    {
        public static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        public static bool Under(string path, string root) => Paths.Equals(path, root) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        public static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        public static string Full(string path) { Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path), "Expected an absolute path."); return Path.GetFullPath(path); }
        public static string Relative(string path)
        {
            Require(path.Length > 0 && !path.Contains('\\') && !path.Contains(':') && !Path.IsPathRooted(path) && path.Split('/').All(part => part.Length > 0 && part is not "." and not "..") &&
                path.All(c => c >= 32 && c != '\u007f'), "Noncanonical portable asset path."); return path;
        }
        public static bool IsHash(string value, int size = 64) => value.Length == size && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        public static bool IsDigest(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
        public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        public static string HashFile(string path) { using FileStream stream = File.OpenRead(Full(path)); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
        public static void VerifyFile(string path, long size, string hash, bool noLinks = false)
        {
            if (noLinks) NoLinks(path);
            Require(size >= 0 && IsHash(hash), "Changed or missing Wasm input: " + path);
            // Source links bind their targets separately. Measure the same
            // opened bytes that are hashed, rather than a Unix link's length.
            using FileStream stream = File.OpenRead(Full(path));
            Require(stream.Length == size && Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() == hash,
                "Changed or missing Wasm input: " + path);
        }
        public static void NoLinks(string path)
        {
            string full = Full(path), current = Path.GetPathRoot(full)!;
            foreach (string part in full[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Runtime product must not traverse a link: " + current);
            }
        }
        public static string String(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidDataException("Null field: " + name);
        public static string[] Strings(JsonElement value) => value.EnumerateArray().Select(item => item.GetString() ?? throw new InvalidDataException("Null string entry.")).ToArray();
        public static void Fields(JsonElement value, params string[] fields)
        {
            Require(value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Select(item => item.Name).OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(fields.OrderBy(x => x, StringComparer.Ordinal)), "Unexpected JSON fields.");
        }
        public static JsonDocument Parse(byte[] raw)
        {
            JsonDocument document = JsonDocument.Parse(json: new UTF8Encoding(false, true).GetString(raw));
            try { Validate(document.RootElement); return document; } catch { document.Dispose(); throw; }
        }
        private static void Validate(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject()) { Require(seen.Add(property.Name), "Duplicate JSON field."); Quote(property.Name); Validate(property.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (JsonElement item in value.EnumerateArray()) Validate(item);
            else if (value.ValueKind == JsonValueKind.String) Quote(value.GetString()!);
            else if (value.ValueKind == JsonValueKind.Number) Require(value.TryGetInt64(out _), "Only integer JSON numbers are supported.");
            else Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null, "Unsupported JSON token.");
        }
        public static string CanonicalHash(object value) => Hash(CanonicalBytes(JsonSerializer.SerializeToElement(value)));
        public static byte[] CanonicalBytes(JsonElement value) => new UTF8Encoding(false, true).GetBytes(Canonical(value));
        private static string Canonical(JsonElement value) => value.ValueKind switch {
            JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(p => p.Name, Utf8Keys.Instance).Select(p => Quote(p.Name) + ":" + Canonical(p.Value))) + "}",
            JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
            JsonValueKind.String => Quote(value.GetString()!), JsonValueKind.Number => value.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Null => "null", _ => throw new InvalidDataException("Unsupported JSON value.") };
        private static string Quote(string value)
        {
            var output = new StringBuilder("\"");
            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];
                if (char.IsSurrogate(c)) { Require(char.IsHighSurrogate(c) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]), "Unpaired JSON surrogate."); output.Append(c).Append(value[++index]); }
                else output.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", < ' ' => "\\u" + ((int)c).ToString("x4"), _ => c.ToString() });
            }
            return output.Append('"').ToString();
        }
        private sealed class Utf8Keys : IComparer<string>
        {
            internal static readonly Utf8Keys Instance = new();
            public int Compare(string? left, string? right)
            {
                byte[] a = Encoding.UTF8.GetBytes(left!), b = Encoding.UTF8.GetBytes(right!);
                for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
                return a.Length.CompareTo(b.Length);
            }
        }
    }
}
