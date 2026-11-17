using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;
using UnrealSharp.Automation.BuildCommands;
using UnrealSharp.Shared;
using UnrealSharp.Wasm;

namespace UnrealSharp.Automation.Utilities;

internal sealed record WasmPreparationTools(string DotnetHost, string SdkRoot, string EmConfig,
    string EmCache, string LlvmRoot, string CMake, string Ninja);

internal sealed record WasmManagedPreparationResult(string ManagedDirectory, string HelperDirectory,
    IReadOnlyList<string> Files, JsonElement Manifest, WasmToolchainPaths ToolchainPaths);

// Preparation only: the coordinator owns Ready publication and releases the
// producer's read lease before invoking the final native UBT/UHT writer.
internal static class WasmManagedPreparation
{
    internal static WasmManagedPreparationResult Build(WasmBuildInput input, UhtProducerSnapshot producer, WasmPreparationTools tools) =>
        new Preparation(input, producer, tools).Build();

    private sealed record ObservedFile(string Path, long Bytes, string Sha256, string Role);
    private sealed record AssemblyInput(string Name, string Path, string Mvid, string[] References, string[] ReverseKeys);
    private sealed class Preparation
    {
        private readonly WasmBuildInput _input;
        private readonly UhtProducerSnapshot _producer;
        private readonly WasmPreparationTools _tools;
        private readonly string _directory, _managed, _helpers, _artifacts, _capture, _templates;
        private readonly Dictionary<string, ObservedFile> _before = new(UhtProducerReadPaths.Comparer);
        private readonly List<object> _commands = [];
        private readonly List<string> _outputs = [];
        private readonly HashSet<string> _evaluatedProjects = new(UhtProducerReadPaths.Comparer);
        private readonly Dictionary<string, string> _projectOutputs = new(UhtProducerReadPaths.Comparer);
        private readonly Dictionary<string, string> _environment = new(StringComparer.OrdinalIgnoreCase);
        private readonly WasmFrozenPaths _sourcePaths = WasmFrozenPaths.CreateToolchain();
        private string _dotnetSdk = "";

        internal Preparation(WasmBuildInput input, UhtProducerSnapshot producer, WasmPreparationTools tools)
        {
            _input = input; _producer = producer; _tools = tools;
            _directory = Path.Combine(Path.GetDirectoryName(input.AppArchive)!, "AppPreparation");
            _managed = Path.Combine(_directory, "Managed"); _helpers = Path.Combine(_directory, "Helpers");
            _artifacts = Path.Combine(_directory, "Artifacts");
            _capture = Path.Combine(_directory, "CompilerInputs", Guid.NewGuid().ToString("N"));
            _templates = Path.Combine(input.Profile.PluginDirectory, "Build", "Runtime", "Wasm", "app-glue");
        }

        internal WasmManagedPreparationResult Build()
        {
            Validate(); // A missing product/profile/tool never reaches output creation.
            CaptureInitialInputs();
            MakeDirectory(_directory); MakeDirectory(_capture);
            string[] glue = _producer.Inventories.SelectMany(root => root.Value.Keys.Where(path => path.EndsWith(".csproj", StringComparison.Ordinal))
                .Select(path => Path.Combine(root.Key, path))).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var command = new PackageProject { Params = ["Project=" + _input.Profile.ProjectFile] };
            var target = new ManagedBuildTarget(UnrealTargetPlatform.Parse("Wasm"), TargetType.Game,
                Enum.Parse<UnrealTargetConfiguration>(_input.Profile.Configuration));
            // Existing target-aware discovery is read-only. No package/publish command is executed.
            UnrealSharpSettingsUtilities.Reset();
            string[] users = command.GetManagedProjectFiles(target).Where(project => !ProjectUtilities.IsEditorOnlyProject(project.FullName))
                .Select(project => project.FullName).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            string baseProject = Path.Combine(_input.Profile.PluginDirectory, "Managed", "UnrealSharp", "UnrealSharp.Plugins", "UnrealSharp.Plugins.csproj");
            foreach (string project in users.Concat(glue)) CaptureSourceTree(Path.GetDirectoryName(project)!);
            var built = new List<string>();
            string pluginOutput = CompileProject(baseProject, null); built.Add(pluginOutput);
            string packageDirectory = Path.GetDirectoryName(pluginOutput)!;
            foreach (string project in glue.Concat(users).Distinct(UhtProducerReadPaths.Comparer)) built.Add(CompileProject(project, packageDirectory));
            ConsumeCompilerInputs([baseProject, ..glue, ..users]);
            CheckUnchanged();

            WasmRuntimeProduct product = _input.RuntimeProduct;
            string[] candidates = built.SelectMany(path => Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.dll", SearchOption.TopDirectoryOnly))
                .Concat(product.ManagedFiles).Append(product.CoreLib).Distinct(UhtProducerReadPaths.Comparer).ToArray();
            string[] baseNames = ["UnrealSharp", "UnrealSharp.Core", "UnrealSharp.Binds", "UnrealSharp.Log", "UnrealSharp.Plugins"];
            string[] rootFiles = built.Concat(baseNames.Select(name => Path.Combine(packageDirectory, name + ".dll"))).Append(product.CoreLib).ToArray();
            AssemblyInput[] closure = ResolveClosure(rootFiles, candidates, product);
            MakeDirectory(_managed);
            foreach (AssemblyInput assembly in closure)
            {
                Observe(assembly.Path, "managed-helper-input");
                Copy(assembly.Path, Path.Combine(_managed, assembly.Name + ".dll"));
            }
            // A reused preparation directory must never stage a previous app's extra DLL.
            var names = closure.Select(assembly => assembly.Name + ".dll").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(_managed, "*.dll"))
                if (!names.Contains(Path.GetFileName(file))) { Safe(file); File.Delete(file); }
            EmitLoadOrder(glue, "GlueCode", LoadOrderUtilities.GlueLoadOrderPriority);
            EmitLoadOrder(users, "UserCode", LoadOrderUtilities.UserLoadOrderPriority);

            var closureNames = closure.Select(assembly => assembly.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] references = product.ManagedFiles.Where(path => !closureNames.Contains(ReadAssembly(path).Name)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            foreach (string path in references) Observe(path, "helper-reference");
            string[] modules = product.Archives.Concat(product.JavaScriptLibraries).Select(Path.GetFileNameWithoutExtension)
                .Select(name => name!.StartsWith("lib", StringComparison.Ordinal) ? name[3..] : name).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            string helperManifest = Path.Combine(_directory, "helper-inputs.json");
            Write(helperManifest, JsonSerializer.Serialize(new {
                InputAssemblies = closure.Select(assembly => Path.Combine(_managed, assembly.Name + ".dll")).ToArray(),
                ReferenceAssemblies = references, PInvokeModules = modules,
                Assemblies = closure.Select(assembly => new { assembly.Name, Path = Path.Combine(_managed, assembly.Name + ".dll"), assembly.Mvid })
            }, new JsonSerializerOptions { WriteIndented = true }));
            Observe(helperManifest, "helper-manifest");
            foreach (string path in product.HelperToolFiles) Observe(path, "helper-cli");
            foreach (string path in Directory.EnumerateFiles(_managed, "*.dll")) Observe(path, "staged-helper-input");
            MakeDirectory(_helpers);
            Run(_tools.DotnetHost, [product.HelperToolEntry, helperManifest, _helpers], "helpers");
            string[] helpers = ["callhelpers-pinvoke.cpp", "callhelpers-reverse.cpp", "callhelpers-interp-to-managed.cpp"];
            VerifyHelpers(closure, helpers);
            foreach (string name in helpers) { string path = Path.Combine(_helpers, name); Observe(path, "native-helper-source"); _outputs.Add(path); }
            BuildArchive(helpers);
            CheckUnchanged();
            string receipt = Path.Combine(_directory, "preparation.json");
            var outputs = _outputs.Distinct(UhtProducerReadPaths.Comparer).Select(path => Describe(path, "output")).ToArray();
            JsonElement manifest = JsonSerializer.SerializeToElement(new {
                Scope = "Plain Game IL closure and complete application helpers/archive; not final UE link or Ready",
                Profile = _input.Profile, _input.NativeProductKey, _input.RuntimeProductSha256,
                Uht = new { _producer.ManifestHash, _producer.Context, _producer.Inputs, _producer.Transaction },
                Inputs = _before.Values.OrderBy(file => file.Path, StringComparer.Ordinal), Outputs = outputs,
                Assemblies = closure, Commands = _commands, DotnetSdk = _dotnetSdk,
                DynamicLoads = "Only discovered Game projects and generated glue are explicit roots; undeclared dynamic assemblies are not inferred."
            });
            Write(receipt, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            CheckUnchanged();
            string[] files = _before.Keys.Concat(_outputs).Append(receipt).Distinct(UhtProducerReadPaths.Comparer).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            return new(_managed, _helpers, Array.AsReadOnly(files), manifest.Clone(), _sourcePaths.SnapshotToolchain());
        }

        private void Validate()
        {
            CheckUnchanged();
            var profile = _input.Profile; var request = _producer.Request;
            Require(profile.Platform == "Wasm" && profile.TargetType == "Game" && profile.Architecture == "wasm64" && profile.LowerMemory64 && profile.Simd128,
                "Managed preparation requires the supported lowered Wasm64 Game profile.");
            Require(request.Platform == profile.Platform && request.TargetName == profile.TargetName && request.Configuration.ToString() == profile.Configuration &&
                request.Architectures == profile.Architecture && UhtProducerReadPaths.Comparer.Equals(request.ProjectFile, profile.ProjectFile) &&
                UhtProducerReadPaths.Comparer.Equals(request.PluginDirectory, profile.PluginDirectory) && UhtProducerReadPaths.Comparer.Equals(request.EngineDirectory, profile.EngineDirectory),
                "Managed preparation requires a real matching Wasm Game UHT producer.");
            foreach (string file in new[] { _tools.DotnetHost, _tools.EmConfig, _tools.CMake, _tools.Ninja }) Require(Path.IsPathFullyQualified(file) && File.Exists(file), "Missing explicit preparation tool/config: " + file);
            Require(UhtProducerReadPaths.Comparer.Equals(Path.GetFullPath(_tools.LlvmRoot), Path.GetFullPath(Path.Combine(_tools.SdkRoot, "upstream", "bin"))),
                "Application helpers must use the UE-selected SDK's direct LLVM tools.");
            Require(File.ReadAllText(Path.Combine(_tools.SdkRoot, "upstream", "emscripten", "emscripten-version.txt")).Trim().Trim('"') == "6.0.10", "Unsupported Emscripten SDK version.");
            _dotnetSdk = Directory.EnumerateDirectories(Path.Combine(Path.GetDirectoryName(_tools.DotnetHost)!, "sdk"))
                .Where(path => Path.GetFileName(path).StartsWith("12.", StringComparison.Ordinal) && File.Exists(Path.Combine(path, "dotnet.dll")))
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal).LastOrDefault() ?? throw new InvalidDataException("The explicit dotnet host has no .NET12 SDK.");
            _environment["EM_CONFIG"] = _tools.EmConfig; _environment["EM_CACHE"] = _tools.EmCache;
            _environment["EM_LLVM_ROOT"] = _tools.LlvmRoot;
            // The SDK's Unix launchers select Python through EMSDK_PYTHON,
            // not a PYTHON setting in .emscripten. Freeze the selected executables
            // explicitly so CMake's child tools cannot silently fall back to PATH.
            string config = File.ReadAllText(_tools.EmConfig);
            _environment["EMSDK_PYTHON"] = ReadHostTool(config, "PYTHON", "EMSDK_PYTHON", _tools.EmConfig);
            _environment["EM_NODE_JS"] = ReadHostTool(config, "NODE_JS", "EM_NODE_JS", _tools.EmConfig);
            foreach (string name in new[] { "EMSDK_PYTHON", "EM_NODE_JS" }) ReadSafe(_environment[name]);
            _environment["MSBuildExtensionsPath"] = _dotnetSdk; _environment["MSBUILD_EXE_PATH"] = Path.Combine(_dotnetSdk, "MSBuild.dll");
            _environment["MSBuildSDKsPath"] = Path.Combine(_dotnetSdk, "Sdks"); _environment["DOTNET_ROLL_FORWARD"] = "LatestMinor";
        }

        private void CaptureInitialInputs()
        {
            foreach (string path in _producer.FileHashes.Keys) Observe(path, "uht-producer");
            Observe(_input.InputFile, "build-input"); Observe(_input.RuntimeProductFile, "runtime-descriptor");
            foreach (WasmProductAsset asset in _input.RuntimeProduct.Assets) Observe(_input.RuntimeProduct.RequireAsset(asset.Path), "runtime-asset");
            foreach (string path in new[] { _tools.DotnetHost, _tools.EmConfig, _tools.CMake, _tools.Ninja,
                Path.Combine(_dotnetSdk, "dotnet.dll"), Path.Combine(_dotnetSdk, "MSBuild.dll"), typeof(WasmManagedPreparation).Assembly.Location }) Observe(path, "tool");
            foreach (string name in new[] { "clang", "clang++", "llvm-ar", "llvm-ranlib", "llvm-nm", "wasm-ld" }) Observe(HostTool(_tools.LlvmRoot, name), "ue-llvm-tool");
            foreach (string path in Directory.EnumerateFiles(Path.Combine(_dotnetSdk, "Roslyn", "bincore"), "*.dll")) Observe(path, "compiler-tool");
            string emscripten = Path.Combine(_tools.SdkRoot, "upstream", "emscripten");
            foreach (string name in new[] { "emcc", "em++", "emar", "emranlib", "embuilder", "em-config", "emnm" }) Observe(HostTool(emscripten, name), "emscripten-launcher");
            foreach (string name in new[] { "emscripten-version.txt", "emcc.py", "em++.py", "emar.py", "emranlib.py", "embuilder.py", "em-config.py", "tools/emnm.py", "tools/config.py", "tools/shared.py", "tools/system_libs.py", "cmake/Modules/Platform/Emscripten.cmake" }) Observe(Path.Combine(emscripten, name), "emscripten-toolchain");
            CaptureTree(Path.Combine(emscripten, "system", "include"), "sdk-source-header");
            foreach (string name in new[] { "EMSDK_PYTHON", "EM_NODE_JS" }) Observe(_environment[name], "emscripten-host-tool");
            foreach (string version in Directory.EnumerateDirectories(Path.Combine(_tools.SdkRoot, "upstream", "lib", "clang"))) CaptureTree(Path.Combine(version, "include"), "compiler-header");
            CaptureSourceTree(Path.Combine(_input.Profile.PluginDirectory, "Managed"));
            CaptureSourceTree(Path.Combine(_input.Profile.PluginDirectory, "Build", "Scripts"));
            foreach (string path in Directory.EnumerateFiles(_input.Profile.PluginDirectory, "*.props")) Observe(path, "plugin-build-input");
            Observe(Path.Combine(_input.Profile.PluginDirectory, "Source", "ThirdParty", "UnrealSharpWasmRuntime", "UnrealSharpWasmRuntime.Build.cs"), "shared-prelink-contract");
            CaptureTree(_templates, "preparation-template");
        }

        private static string HostTool(string directory, string name) =>
            Path.Combine(directory, name + (OperatingSystem.IsWindows() ? ".exe" : ""));

        private static string ReadHostTool(string config, string setting, string environmentName, string configFile)
        {
            string? path = Environment.GetEnvironmentVariable(environmentName);
            if (string.IsNullOrEmpty(path))
            {
                MatchCollection matches = Regex.Matches(config, "(?m)^" + setting + "\\s*=\\s*(['\"])([^'\"\\r\\n]+)\\1[ \\t]*\\r?$");
                Require(matches.Count == 1, "The selected Emscripten SDK requires one explicit " + environmentName + " executable or one literal " + setting + " config setting.");
                path = matches[0].Groups[2].Value.Replace("$CFGDIR", Path.GetDirectoryName(configFile)!, StringComparison.Ordinal);
            }
            Require(Path.IsPathFullyQualified(path) && File.Exists(path), "Missing explicit Emscripten host executable: " + environmentName);
            return Path.GetFullPath(path);
        }

        private string CompileProject(string project, string? packageDirectory)
        {
            Observe(project, "managed-project");
            string[] arguments = ["-p:UETargetType=Game", "-p:UEBuildConfig=" + _input.Profile.Configuration,
                "-p:TargetPlatform=Wasm", "-p:TargetArchitecture=wasm64",
                "-p:UseDefaultOutputPath=true", "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=" + _artifacts,
                "-p:BuildingAOT=false", "-p:PublishAot=false", "-p:PublishReadyToRun=false", "-p:SelfContained=false", "-p:RuntimeIdentifier=",
                "-p:UnrealSharpWasmInputRecords=" + Path.Combine(_capture, _commands.Count.ToString("D3"))];
            if (packageDirectory != null) arguments = [..arguments, "-p:UnrealSharpPackageDirectory=" + packageDirectory];
            string configuration = _input.Profile.Configuration is "Debug" or "DebugGame" ? "Debug" : "Release";
            ValidateManagedGraph(project, ["-p:Configuration=" + configuration, ..arguments]);
            arguments = [..arguments, "-p:CustomBeforeMicrosoftCommonTargets=" + Path.Combine(_templates, "ManagedInputs.targets")];
            RunDotnet(["build", project, "--configuration", configuration, ..arguments], "managed-" + Path.GetFileNameWithoutExtension(project));
            string text = RunDotnet(["msbuild", project, "-p:Configuration=" + configuration, ..arguments, "-getProperty:TargetPath,AssemblyName"], "target-" + Path.GetFileNameWithoutExtension(project));
            using var document = JsonDocument.Parse(text);
            string output = document.RootElement.GetProperty("Properties").GetProperty("TargetPath").GetString()!;
            Require(File.Exists(output), "Normal managed build did not produce its evaluated TargetPath: " + project);
            _projectOutputs[Path.GetFullPath(project)] = output;
            _outputs.Add(output); return output;
        }

        private void ConsumeCompilerInputs(IEnumerable<string> projects)
        {
            var captured = new HashSet<string>(UhtProducerReadPaths.Comparer);
            foreach (string file in Directory.EnumerateFiles(_capture, "*.inputs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file); Require(lines.Length >= 3 && lines[0].StartsWith("PROJECT|", StringComparison.Ordinal) &&
                    lines[1] == "PROFILE|Game|" + _input.Profile.Configuration + "|Wasm|wasm64||false|false", "Compiler input capture/profile is missing or incompatible.");
                captured.Add(lines[0][8..]);
                foreach (string line in lines.Skip(2)) { int split = line.LastIndexOf('|'); Require(split > 0, "Invalid compiler input record."); Observe(line[..split], "pre-compile-input", line[(split + 1)..]); }
                _outputs.Add(file);
            }
            Require(projects.All(project => captured.Contains(project)), "A managed project skipped the required pre-compiler input capture.");
        }

        private void ValidateManagedGraph(string project, string[] arguments)
        {
            if (!_evaluatedProjects.Add(Path.GetFullPath(project))) return;
            string text = RunDotnet(["msbuild", project, ..arguments,
                "-getProperty:MSBuildAllProjects,PreBuildEvent,PostBuildEvent,CustomBeforeMicrosoftCommonTargets,CustomAfterMicrosoftCommonTargets", "-getItem:ProjectReference"],
                "evaluate-" + Path.GetFileNameWithoutExtension(project));
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement properties = document.RootElement.GetProperty("Properties");
            foreach (string property in new[] { "PreBuildEvent", "PostBuildEvent", "CustomBeforeMicrosoftCommonTargets", "CustomAfterMicrosoftCommonTargets" })
            {
                string value = properties.GetProperty(property).GetString() ?? "";
                string sdkHook = Path.Combine(_dotnetSdk, "vCurrent", property == "CustomBeforeMicrosoftCommonTargets"
                    ? "Custom.Before.Microsoft.Common.targets" : "Custom.After.Microsoft.Common.targets");
                bool missingSdkHook = (property is "CustomBeforeMicrosoftCommonTargets" or "CustomAfterMicrosoftCommonTargets") &&
                    Path.IsPathFullyQualified(value) && UhtProducerReadPaths.Comparer.Equals(Path.GetFullPath(value), sdkHook) && !File.Exists(value);
                Require(string.IsNullOrWhiteSpace(value) || missingSdkHook, "Managed prelink cannot replace or execute custom build hooks while UHT roots are read-locked: " + project + " (" + property + ")");
            }
            foreach (string path in properties.GetProperty("MSBuildAllProjects").GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries).Append(project).Distinct(UhtProducerReadPaths.Comparer))
            {
                Observe(path, "evaluated-msbuild-import");
                if (UhtProducerReadPaths.IsUnder(Path.GetFullPath(path), _dotnetSdk)) continue;
                XDocument xml = XDocument.Load(path);
                Require(!xml.Descendants().Any(element => element.Name.LocalName is "Exec" or "MSBuild" or "UsingTask"),
                    "Custom managed tasks need a separately proven non-UBT preparation workflow: " + path);
            }
            foreach (JsonElement reference in document.RootElement.GetProperty("Items").GetProperty("ProjectReference").EnumerateArray())
                ValidateManagedGraph(reference.GetProperty("FullPath").GetString()!, arguments);
        }

        private static AssemblyInput[] ResolveClosure(IEnumerable<string> roots, IEnumerable<string> candidates, WasmRuntimeProduct product)
        {
            var map = new Dictionary<string, AssemblyInput>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in candidates)
            {
                AssemblyInput assembly = ReadAssembly(path);
                if (map.TryGetValue(assembly.Name, out AssemblyInput? previous)) Require(Hash(previous.Path) == Hash(path), "Ambiguous managed assembly identity: " + assembly.Name);
                else map.Add(assembly.Name, assembly);
            }
            foreach (JsonElement framework in product.Product.GetProperty("Managed").GetProperty("Framework").EnumerateArray())
                Require(map.TryGetValue(framework.GetProperty("Name").GetString()!, out AssemblyInput? assembly) && assembly.Mvid.Equals(framework.GetProperty("Mvid").GetString(), StringComparison.OrdinalIgnoreCase), "Framework assembly does not match selected native product.");
            Require(Hash(product.CoreLib).Equals(product.Product.GetProperty("Managed").GetProperty("CoreLibSha256").GetString(), StringComparison.OrdinalIgnoreCase), "CoreLib does not match the native product.");
            var pending = new Queue<string>(roots.Select(path => ReadAssembly(path).Name)); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (pending.TryDequeue(out string? name))
            {
                if (!visited.Add(name)) continue;
                Require(map.TryGetValue(name, out AssemblyInput? assembly), "Missing Game AssemblyRef: " + name);
                foreach (string reference in assembly!.References) pending.Enqueue(reference);
            }
            return visited.OrderBy(name => name, StringComparer.Ordinal).Select(name => map[name]).ToArray();
        }

        private static AssemblyInput ReadAssembly(string path)
        {
            using var image = new PEReader(File.OpenRead(path));
            Require(image.HasMetadata && image.PEHeaders.CorHeader is { } header && (header.Flags & CorFlags.ILOnly) != 0 && header.ManagedNativeHeaderDirectory.Size == 0, "Expected plain managed IL: " + path);
            MetadataReader metadata = image.GetMetadataReader();
            string name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            var reverse = new List<string>();
            foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    MethodDefinition method = metadata.GetMethodDefinition(methodHandle);
                    bool callback = method.GetCustomAttributes().Any(handle => AttributeName(metadata, metadata.GetCustomAttribute(handle)) == "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute");
                    if (!callback) continue;
                    BlobReader signature = metadata.GetBlobReader(method.Signature); SignatureHeader signatureHeader = signature.ReadSignatureHeader();
                    if (signatureHeader.IsGeneric) signature.ReadCompressedInteger(); int count = signature.ReadCompressedInteger();
                    string ns = type.GetDeclaringType().IsNil ? metadata.GetString(type.Namespace) : "";
                    reverse.Add(metadata.GetString(method.Name) + "#" + count + ":" + name + ":" + ns + ":" + metadata.GetString(type.Name));
                }
            }
            Require(!metadata.GetAssemblyDefinition().GetCustomAttributes().Any(handle => AttributeName(metadata, metadata.GetCustomAttribute(handle)) == "System.Runtime.CompilerServices.ReferenceAssemblyAttribute"), "Reference assembly cannot be staged as runtime IL: " + path);
            return new(name, Path.GetFullPath(path), metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString(),
                metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray(), reverse.ToArray());
        }

        private static string AttributeName(MetadataReader metadata, CustomAttribute attribute)
        {
            EntityHandle type = attribute.Constructor.Kind == HandleKind.MemberReference ? metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent :
                metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
            if (type.Kind == HandleKind.TypeReference) { var value = metadata.GetTypeReference((TypeReferenceHandle)type); return metadata.GetString(value.Namespace) + "." + metadata.GetString(value.Name); }
            if (type.Kind == HandleKind.TypeDefinition) { var value = metadata.GetTypeDefinition((TypeDefinitionHandle)type); return metadata.GetString(value.Namespace) + "." + metadata.GetString(value.Name); }
            return "";
        }

        private void VerifyHelpers(AssemblyInput[] closure, string[] names)
        {
            foreach (string name in names) { string path = Path.Combine(_helpers, name); Require(File.Exists(path) && File.ReadAllText(path).Contains("static_assert(sizeof(void*) == 8", StringComparison.Ordinal), "Missing real Wasm64 generated helper: " + name); }
            string pinvoke = File.ReadAllText(Path.Combine(_helpers, names[0])), reverse = File.ReadAllText(Path.Combine(_helpers, names[1]));
            Require(pinvoke.Contains("callhelpers_pinvoke_override", StringComparison.Ordinal) && pinvoke.Contains("ResolveRuntimeHelperEntrypoint", StringComparison.Ordinal), "Incomplete application PInvoke/QCall replacement.");
            var frameworkNames = _input.RuntimeProduct.ManagedFiles.Append(_input.RuntimeProduct.CoreLib).Select(path => ReadAssembly(path).Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string key in closure.Where(assembly => !frameworkNames.Contains(assembly.Name)).SelectMany(assembly => assembly.ReverseKeys))
                Require(reverse.Contains("\"" + key.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"", StringComparison.Ordinal), "Application UCO is missing from generated helpers: " + key);
        }

        private void BuildArchive(string[] helperNames)
        {
            WasmRuntimeProduct product = _input.RuntimeProduct;
            string data = Path.Combine(_directory, "app-inputs.cmake");
            string Set(string name, IEnumerable<string> values) => "set(" + name + "\n" + string.Join("\n", values.Select(value => "  " + CMakeQuote(value))) + "\n)\n";
            Write(data, Set("UNREALSHARP_HELPER_SOURCES", helperNames.Select(name => Path.Combine(_helpers, name))) +
                Set("UNREALSHARP_HELPER_INCLUDES", product.HelperIncludeDirectories) +
                Set("UNREALSHARP_HELPER_DEFINITIONS", product.Product.GetProperty("Native").GetProperty("HelperDefinitions").EnumerateArray().Select(value => value.GetString()!)) +
                Set("UNREALSHARP_HELPER_OPTIONS", product.CompileOptions) + Set("UNREALSHARP_HELPER_FORCE_INCLUDE", [product.HelperForceInclude]) +
                Set("UNREALSHARP_ARCHIVE_DIRECTORY", [Path.GetDirectoryName(_input.AppArchive)!]));
            Observe(data, "cmake-input");
            string build = Path.Combine(_directory, "Native");
            Run(HostTool(Path.Combine(_tools.SdkRoot, "upstream", "emscripten"), "embuilder"), ["build", "sysroot"], "emscripten-sysroot");
            string sysroot = Path.Combine(_tools.EmCache, "sysroot", "include");
            Require(Directory.Exists(sysroot), "Selected EM_CACHE has no initialized sysroot headers.");
            CaptureTree(sysroot, "actual-em-cache-header");
            Run(_tools.CMake, ["-S", _templates, "-B", build, "-G", "Ninja", "-DCMAKE_MAKE_PROGRAM=" + _tools.Ninja,
                "-DCMAKE_TOOLCHAIN_FILE=" + Path.Combine(_tools.SdkRoot, "upstream", "emscripten", "cmake", "Modules", "Platform", "Emscripten.cmake"),
                "-DCMAKE_BUILD_TYPE=Release", "-DCMAKE_CXX_FLAGS_RELEASE=-O2 -DNDEBUG", "-DCMAKE_EXPORT_COMPILE_COMMANDS=ON", "-DUNREALSHARP_APP_INPUTS=" + data], "cmake-configure");
            CheckUnchanged();
            Run(_tools.CMake, ["--build", build, "--target", "UnrealSharpWasmApplication", "--parallel", "2"], "cmake-build");
            Require(File.Exists(_input.AppArchive), "Normal CMake graph did not produce the application archive.");
            string symbols = Run(HostTool(_tools.LlvmRoot, "llvm-nm"), ["--defined-only", "--demangle", _input.AppArchive], "archive-symbols");
            foreach (string name in new[] { "callhelpers_pinvoke_override", "ResolveRuntimeHelperEntrypoint", "g_ReverseThunks", "g_portableCallHelperThunks" })
                Require(Regex.IsMatch(symbols, @"(?m)^.*\b" + Regex.Escape(name) + @"(?:\(|\s|$)"), "Application archive is missing generated symbol: " + name);
            _outputs.AddRange([_input.AppArchive, Path.Combine(build, "CMakeCache.txt"), Path.Combine(build, "compile_commands.json"), data]);
        }

        private static List<string> ResolveLoadOrderFiles(IEnumerable<string> projects, IReadOnlyDictionary<string, string> outputs, string managedDirectory)
        {
            var files = new List<string>();
            foreach (string project in projects.Distinct(UhtProducerReadPaths.Comparer))
            {
                Require(outputs.TryGetValue(Path.GetFullPath(project), out string? output), "No evaluated managed output for load-order project: " + project);
                string file = Path.Combine(managedDirectory, ReadAssembly(output!).Name + ".dll");
                Require(File.Exists(file), "Missing staged managed load-order assembly: " + file);
                files.Add(file);
            }
            return files.Distinct(UhtProducerReadPaths.Comparer).ToList();
        }

        private void EmitLoadOrder(string[] projects, string name, int priority)
        {
            List<string> files = ResolveLoadOrderFiles(projects, _projectOutputs, _managed);
            if (files.Count == 0)
            {
                string stale = Path.Combine(_managed, AssemblyUtilities.MakeLoadOrderFileName(name));
                Safe(stale); if (File.Exists(stale)) File.Delete(stale);
                return;
            }
            AssemblyUtilities.EmitLoadOrder(files, _managed, new LoadOrderOptions { Collectible = false, Priority = priority }, name);
            _outputs.Add(Path.Combine(_managed, AssemblyUtilities.MakeLoadOrderFileName(name)));
        }

        private string RunDotnet(string[] arguments, string label) => Run(_tools.DotnetHost, [Path.Combine(_dotnetSdk, "dotnet.dll"), ..arguments], label);
        private string Run(string executable, string[] arguments, string label)
        {
            CheckUnchanged();
            string log = Path.Combine(_directory, "Logs", _commands.Count.ToString("D3") + "-" + label + ".log"); MakeDirectory(Path.GetDirectoryName(log)!);
            var info = new ProcessStartInfo(executable) { WorkingDirectory = _directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            foreach (var entry in _environment) info.Environment[entry.Key] = entry.Value;
            DateTime started = DateTime.UtcNow;
            using Process process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start preparation tool: " + executable);
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync(); process.WaitForExit();
            string stdout = output.GetAwaiter().GetResult(), stderr = errors.GetAwaiter().GetResult(); Write(log, stdout + stderr);
            _commands.Add(new { Executable = executable, Arguments = arguments, ExitCode = process.ExitCode, StartedUtc = started, EndedUtc = DateTime.UtcNow, Log = log });
            Require(process.ExitCode == 0, "Preparation tool failed (" + process.ExitCode + "): " + log);
            _outputs.Add(log); CheckUnchanged(); return stdout.Trim();
        }

        private void Observe(string path, string role, string? expected = null)
        {
            ReadSafe(path); ObservedFile file = Describe(path, role); _sourcePaths.EnsureRead(_input, Path.GetFullPath(path));
            Require(expected == null || file.Sha256.Equals(expected, StringComparison.OrdinalIgnoreCase), "Compiler input changed after its pre-build capture: " + path);
            if (_before.TryGetValue(file.Path, out ObservedFile? previous)) Require(previous.Bytes == file.Bytes && previous.Sha256 == file.Sha256, "Preparation input changed: " + path);
            else _before.Add(file.Path, file);
        }
        private static ObservedFile Describe(string path, string role) => new(Path.GetFullPath(path), new FileInfo(path).Length, Hash(path), role);
        private static string Hash(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
        private void CheckUnchanged()
        {
            _input.RequireUnchanged(); _producer.RequireUnchanged();
            _sourcePaths.Verify();
            foreach (ObservedFile file in _before.Values) { _sourcePaths.EnsureRead(_input, file.Path); Require(File.Exists(file.Path) && new FileInfo(file.Path).Length == file.Bytes && Hash(file.Path) == file.Sha256, "Preparation input changed: " + file.Path); _sourcePaths.EnsureRead(_input, file.Path); }
            _sourcePaths.Verify();
        }
        private void CaptureSourceTree(string directory) => CaptureTree(directory, "source", sourceOnly: true);
        private void CaptureTree(string directory, string role, bool sourceOnly = false, HashSet<string>? active = null)
        {
            if (!Directory.Exists(directory)) { Safe(directory); return; }
            string physical = ReadSafe(directory); active ??= new HashSet<string>(WasmData.Paths);
            Require(active.Add(physical), "Cyclic toolchain source directory: " + directory);
            try
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    ReadSafe(path);
                    if (Directory.Exists(path)) { if (Path.GetFileName(path) is not ("bin" or "obj" or ".git")) CaptureTree(path, role, sourceOnly, active); }
                    else if (!sourceOnly || new[] { ".cs", ".csproj", ".props", ".targets", ".json", ".config", ".resx" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) Observe(path, role);
                }
            }
            finally { active.Remove(physical); }
        }
        private string ReadSafe(string path)
        {
            path = Path.GetFullPath(path);
            // This view is for explicitly selected toolchain sources only.
            // Writes and Portable runtime members retain the strict input view.
            bool toolchain = !WasmData.Under(path, _input.Profile.BuildDirectory) && !WasmData.Under(path, _input.RuntimeProduct.RootDirectory) &&
                (new[] { _tools.SdkRoot, _tools.EmCache, Path.GetDirectoryName(_tools.DotnetHost)! }.Any(root => WasmData.Under(path, root)) ||
                new[] { _tools.CMake, _tools.Ninja }.Concat(_environment.Where(entry => entry.Key is "EMSDK_PYTHON" or "EM_NODE_JS").Select(entry => entry.Value)).Any(tool => WasmData.Paths.Equals(path, tool)));
            if (toolchain)
            {
                string physical = _sourcePaths.CaptureSource(path);
                Require(!WasmData.Under(physical, _input.PhysicalBuildDirectory) && !WasmData.Under(physical, _input.RuntimeProduct.RootDirectory), "Toolchain source link targets a prepared output or Portable runtime member: " + path);
                return physical;
            }
            Safe(path); return path;
        }
        private void Safe(string path) => _input.RequireSafePath(Path.GetFullPath(path));
        private void MakeDirectory(string path) { Safe(path); Directory.CreateDirectory(path); Safe(path); }
        private void Write(string path, string text) { Safe(path); MakeDirectory(Path.GetDirectoryName(path)!); Safe(path); File.WriteAllText(path, text, new UTF8Encoding(false)); _outputs.Add(path); }
        private void Copy(string source, string destination) { Observe(source, "copy-source"); Safe(destination); File.Copy(source, destination, overwrite: true); Require(Hash(source) == Hash(destination), "Managed copy changed bytes."); _outputs.Add(destination); }
        private static string CMakeQuote(string value) => "\"" + value.Replace('\\', '/').Replace("$", "\\$").Replace(";", "\\;").Replace("\"", "\\\"") + "\"";
        private static void Require(bool value, string reason) { if (!value) throw new InvalidDataException(reason); }
    }
}
