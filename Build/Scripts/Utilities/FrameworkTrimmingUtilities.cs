using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnrealBuildTool;
using UnrealSharp.Automation.BuildCommands;

namespace UnrealSharp.Automation.Utilities;

/// <summary>
/// Trims the self-contained .NET framework of a published UnrealSharp game.
/// UnrealSharp, glue and user assemblies are kept unchanged, because native code finds their types and
/// members by name at runtime. Only the framework is trimmed against everything those assemblies reference.
/// </summary>
internal static class FrameworkTrimmingUtilities
{
    private const string HostAssemblyName = "UnrealSharp.Plugins";
    private const string TrimProjectName = "UnrealSharpFrameworkTrim";

    internal static void TrimFramework(string publishFolder, string workingFolder, string runtimeIdentifier, UnrealTargetConfiguration buildConfig)
    {
        string HostDepsPath = Path.Combine(publishFolder, HostAssemblyName + ".deps.json");
        HashSet<string> FrameworkAssemblies = ReadFrameworkAssemblies(HostDepsPath);
        if (FrameworkAssemblies.Count == 0)
        {
            throw new InvalidOperationException($"No self-contained framework assemblies are listed in {HostDepsPath}.");
        }

        List<string> RootAssemblies = Directory.EnumerateFiles(publishFolder, "*.dll")
            .Where(path => !FrameworkAssemblies.Contains(Path.GetFileName(path)) && IsManagedAssembly(path))
            .ToList();

        string ProjectFolder = Path.Combine(workingFolder, "Project");
        string TrimmedFolder = Path.Combine(workingFolder, "Publish");
        if (Directory.Exists(workingFolder))
        {
            Directory.Delete(workingFolder, recursive: true);
        }
        Directory.CreateDirectory(ProjectFolder);

        WriteTrimProject(ProjectFolder, RootAssemblies);

        LoggerUtilities.LogUnrealSharpInfo($"Trimming the .NET framework against {RootAssemblies.Count} managed assemblies.");
        BuildSolution.RunBuild(ProjectFolder, buildConfig, publish: true,
        [
            "--runtime", runtimeIdentifier,
            // The generated project must not pick up settings meant for the user's projects.
            "-p:ImportDirectoryBuildProps=false",
            "-p:ImportDirectoryBuildTargets=false",
            "-p:ImportDirectoryPackagesProps=false",
            $"-p:PublishDir=\"{TrimmedFolder}{Path.DirectorySeparatorChar}\"",
        ]);

        long SizeBefore = 0;
        long SizeAfter = 0;
        foreach (string FrameworkAssembly in FrameworkAssemblies)
        {
            string PublishedPath = Path.Combine(publishFolder, FrameworkAssembly);
            string TrimmedPath = Path.Combine(TrimmedFolder, FrameworkAssembly);
            if (!File.Exists(PublishedPath))
            {
                continue;
            }

            SizeBefore += new FileInfo(PublishedPath).Length;
            if (File.Exists(TrimmedPath))
            {
                File.Copy(TrimmedPath, PublishedPath, overwrite: true);
                SizeAfter += new FileInfo(PublishedPath).Length;
            }
            else
            {
                File.Delete(PublishedPath);
            }
        }

        // hostpolicy requires every managed asset listed in deps.json to exist.
        foreach (string DepsPath in Directory.EnumerateFiles(publishFolder, "*.deps.json"))
        {
            RemoveMissingFrameworkAssets(DepsPath, publishFolder);
        }

        // The runtime must use the feature switch values the framework was trimmed with.
        JsonObject TrimmedProperties = ReadConfigProperties(Path.Combine(TrimmedFolder, TrimProjectName + ".runtimeconfig.json"));
        foreach (string RuntimeConfigPath in Directory.EnumerateFiles(publishFolder, "*.runtimeconfig.json"))
        {
            ApplyConfigProperties(RuntimeConfigPath, TrimmedProperties);
        }

        LoggerUtilities.LogUnrealSharpInfo($"Trimmed framework assemblies from {SizeBefore / (1024 * 1024)} MiB to {SizeAfter / (1024 * 1024)} MiB.");
    }

    private static HashSet<string> ReadFrameworkAssemblies(string depsPath)
    {
        JsonObject Deps = ReadJson(depsPath);
        JsonObject Target = GetRuntimeTarget(Deps);
        HashSet<string> RuntimePacks = GetRuntimePackLibraries(Deps);

        HashSet<string> Assemblies = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string LibraryName, JsonNode? Library) in Target)
        {
            if (RuntimePacks.Contains(LibraryName) && Library?["runtime"] is JsonObject Runtime)
            {
                foreach ((string AssetPath, JsonNode? _) in Runtime)
                {
                    Assemblies.Add(Path.GetFileName(AssetPath));
                }
            }
        }

        return Assemblies;
    }

    private static void RemoveMissingFrameworkAssets(string depsPath, string publishFolder)
    {
        JsonObject Deps = ReadJson(depsPath);
        HashSet<string> RuntimePacks = GetRuntimePackLibraries(Deps);
        bool Changed = false;

        foreach ((string _, JsonNode? Target) in Deps["targets"]!.AsObject())
        {
            foreach ((string LibraryName, JsonNode? Library) in Target!.AsObject())
            {
                if (!RuntimePacks.Contains(LibraryName) || Library?["runtime"] is not JsonObject Runtime)
                {
                    continue;
                }

                foreach (string AssetPath in Runtime.Select(asset => asset.Key).ToList())
                {
                    if (!File.Exists(Path.Combine(publishFolder, Path.GetFileName(AssetPath))))
                    {
                        Runtime.Remove(AssetPath);
                        Changed = true;
                    }
                }
            }
        }

        if (Changed)
        {
            WriteJson(depsPath, Deps);
        }
    }

    private static JsonObject ReadConfigProperties(string runtimeConfigPath)
    {
        JsonObject RuntimeConfig = ReadJson(runtimeConfigPath);
        return RuntimeConfig["runtimeOptions"]?["configProperties"]?.AsObject() ?? new JsonObject();
    }

    private static void ApplyConfigProperties(string runtimeConfigPath, JsonObject properties)
    {
        JsonObject RuntimeConfig = ReadJson(runtimeConfigPath);
        JsonObject RuntimeOptions = RuntimeConfig["runtimeOptions"]?.AsObject()
            ?? throw new InvalidOperationException($"Missing runtimeOptions in {runtimeConfigPath}.");

        if (RuntimeOptions["configProperties"] is not JsonObject ConfigProperties)
        {
            ConfigProperties = new JsonObject();
            RuntimeOptions["configProperties"] = ConfigProperties;
        }

        foreach ((string Name, JsonNode? Value) in properties)
        {
            ConfigProperties[Name] = Value?.DeepClone();
        }

        WriteJson(runtimeConfigPath, RuntimeConfig);
    }

    private static void WriteTrimProject(string projectFolder, List<string> rootAssemblies)
    {
        StringBuilder References = new();
        foreach (string AssemblyPath in rootAssemblies)
        {
            string AssemblyName = Path.GetFileNameWithoutExtension(AssemblyPath);
            References.AppendLine($"    <Reference Include=\"{AssemblyName}\" HintPath=\"{AssemblyPath}\" />");
            References.AppendLine($"    <TrimmerRootAssembly Include=\"{AssemblyName}\" />");
            // Only the framework copies are used, so do not precompile these assemblies.
            References.AppendLine($"    <PublishReadyToRunExclude Include=\"{Path.GetFileName(AssemblyPath)}\" />");
        }

        string Project =
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>{DotNetUtilities.Version}</TargetFramework>
                <AssemblyName>{TrimProjectName}</AssemblyName>
                <ImplicitUsings>disable</ImplicitUsings>
                <SelfContained>true</SelfContained>
                <PublishTrimmed>true</PublishTrimmed>
                <!-- Only trim assemblies that opt in, which is the framework. -->
                <TrimMode>partial</TrimMode>
                <!-- Trimming drops the precompiled code the shipped framework has. Restore it to keep startup time. -->
                <PublishReadyToRun>true</PublishReadyToRun>
                <!-- UnrealSharp starts through hostfxr's load_assembly_and_get_function_pointer. -->
                <_EnableConsumingManagedCodeFromNativeHosting>true</_EnableConsumingManagedCodeFromNativeHosting>
                <!-- Keep the untrimmed default, so reflection-based JSON in game code still works. -->
                <JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>
              </PropertyGroup>
              <ItemGroup>
            {References}  </ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Combine(projectFolder, TrimProjectName + ".csproj"), Project);
        File.WriteAllText(Path.Combine(projectFolder, "Program.cs"), "internal static class Program { private static void Main() { } }\n");
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static JsonObject GetRuntimeTarget(JsonObject deps)
    {
        string TargetName = deps["runtimeTarget"]?["name"]?.GetValue<string>()
            ?? throw new InvalidOperationException("deps.json has no runtime target.");
        return deps["targets"]?[TargetName]?.AsObject()
            ?? throw new InvalidOperationException($"deps.json has no target named {TargetName}.");
    }

    private static HashSet<string> GetRuntimePackLibraries(JsonObject deps)
    {
        HashSet<string> RuntimePacks = new(StringComparer.Ordinal);
        if (deps["libraries"] is JsonObject Libraries)
        {
            foreach ((string LibraryName, JsonNode? Library) in Libraries)
            {
                if (Library?["type"]?.GetValue<string>() == "runtimepack")
                {
                    RuntimePacks.Add(LibraryName);
                }
            }
        }

        return RuntimePacks;
    }

    private static JsonObject ReadJson(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException($"Could not read {path}.");
    }

    private static void WriteJson(string path, JsonNode node)
    {
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
