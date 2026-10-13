using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using EpicGames.Core;
using UnrealBuildTool;

namespace UnrealSharp.Automation.Utilities;

public static class PluginRuntimeUtilities
{
    private const string ManifestName = "UnrealSharp.Runtime.json";

    private static string GetEngineModulesPath(string pluginDirectory, TargetType targetType) =>
        Path.Combine(PathUtilities.GetUhtGeneratedOutputPath(pluginDirectory, targetType), "EngineModules.json");

    public static void StageEngineModules(string pluginDirectory, TargetType targetType, IEnumerable<string> modules)
    {
        string path = GetEngineModulesPath(pluginDirectory, targetType);
        string contents = JsonSerializer.Serialize(modules.OrderBy(module => module, StringComparer.Ordinal));
        if (!File.Exists(path) || File.ReadAllText(path) != contents)
        {
            File.WriteAllText(path, contents);
        }
    }

    public static HashSet<string> GetPackagedEngineModules(string pluginDirectory, TargetType targetType)
    {
        string path = GetEngineModulesPath(pluginDirectory, targetType);
        string[] modules = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Invalid packaged engine module list: {path}");
        return new HashSet<string>(modules, StringComparer.Ordinal);
    }

    public static bool IsInstalled(string pluginDirectory) =>
        PluginDescriptor.FromFile(new FileReference(Path.Combine(pluginDirectory, "UnrealSharp.uplugin"))).bInstalled;

    private static string GetIdentityPath(string pluginDirectory, TargetType targetType) =>
        Path.Combine(pluginDirectory, "Intermediate", "UnrealSharp", "UHT", targetType.ToString(), "Runtime.id");

    // BuildPlugin distributes these source inputs and their identity after UHT has exported each target.
    public static void StageIdentity(string pluginDirectory, TargetType targetType)
    {
        IEnumerable<string> files = new[] { "Managed", Path.Combine("Build", "Scripts") }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(pluginDirectory, folder), "*", SearchOption.AllDirectories))
            .Where(file => !Path.GetRelativePath(pluginDirectory, file).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or "uatbin" or "uatobj" or "Binaries"))
            .Where(file => Path.GetExtension(file) is ".cs" or ".csproj" or ".props" or ".targets" or ".sln" or ".json")
            .Concat(Directory.EnumerateFiles(pluginDirectory, "*.props"))
            .Concat(Directory.EnumerateFiles(PathUtilities.GetUhtGeneratedOutputPath(pluginDirectory, targetType), "*.cs", SearchOption.AllDirectories))
            .Append(Path.Combine(PathUtilities.GetUhtGeneratedOutputPath(pluginDirectory, targetType), "UE5Rules.Defines.props"))
            .Append(GetEngineModulesPath(pluginDirectory, targetType));

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in files.OrderBy(file => Path.GetRelativePath(pluginDirectory, file), StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(pluginDirectory, file).Replace('\\', '/')));
            hash.AppendData(File.ReadAllBytes(file));
        }
        string identityPath = GetIdentityPath(pluginDirectory, targetType);
        string identity = Convert.ToHexString(hash.GetHashAndReset());
        if (!File.Exists(identityPath) || File.ReadAllText(identityPath) != identity)
        {
            File.WriteAllText(identityPath, identity);
        }
    }

    private static string GetCacheDirectory(string pluginDirectory, TargetType targetType, UnrealTargetConfiguration configuration)
    {
        string identityPath = GetIdentityPath(pluginDirectory, targetType);
        if (!File.Exists(identityPath))
        {
            throw new FileNotFoundException("The installed UnrealSharp package is missing its managed runtime identity. Reinstall a complete matching plugin package.", identityPath);
        }
        string identity = File.ReadAllText(identityPath).Trim();
        if (identity.Length != 64 || identity.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"Invalid UnrealSharp runtime identity: {identityPath}");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unrealsharp", "Runtime",
            identity, targetType.ToString(), configuration.ToString());
    }

    public static string EnsureEditorRuntime(string pluginDirectory)
    {
        string buildDirectory = GetCacheDirectory(pluginDirectory, TargetType.Editor, UnrealTargetConfiguration.Development);
        string outputDirectory = Path.Combine(buildDirectory, "bin", DotNetUtilities.Version);
        using FileStream buildLock = AcquireBuildLock(buildDirectory);
        if (IsComplete(outputDirectory))
        {
            LoggerUtilities.LogUnrealSharpInfo($"Using cached UnrealSharp editor runtime: {outputDirectory}");
            return outputDirectory;
        }

        LoggerUtilities.LogUnrealSharpInfo($"Building UnrealSharp editor runtime in user cache: {outputDirectory}");
        BuildCommands.BuildSolution.RunBuild(Path.Combine(pluginDirectory, "Managed", "UnrealSharp", "Runtime"),
            UnrealTargetConfiguration.Development, publish: false, GetInstalledBuildArguments(buildDirectory));
        WriteManifest(outputDirectory);
        return outputDirectory;
    }

    public static IList<string> GetPublishArguments(TargetType targetType,
        UnrealTargetConfiguration configuration, string runtimeIdentifier, string outputDirectory) =>
    [
        "--runtime", runtimeIdentifier,
        "-p:UseDefaultOutputPath=true",
        "-p:PublishSelfContained=true",
        $"-p:UETargetType={targetType}",
        $"-p:UEBuildConfig={configuration}",
        $"-p:PublishDir={outputDirectory}"
    ];

    public static void Publish(string pluginDirectory, TargetType targetType,
        UnrealTargetConfiguration configuration, string runtimeIdentifier, string outputDirectory)
    {
        IList<string> arguments = GetPublishArguments(targetType, configuration, runtimeIdentifier, outputDirectory);
        if (!IsInstalled(pluginDirectory))
        {
            BuildCommands.BuildSolution.RunBuild(Path.Combine(pluginDirectory, "Managed", "UnrealSharp"), configuration, publish: true, arguments);
            return;
        }

        string buildDirectory = Path.Combine(GetCacheDirectory(pluginDirectory, targetType, configuration), runtimeIdentifier);
        string publishDirectory = Path.Combine(buildDirectory, "publish");
        using FileStream buildLock = AcquireBuildLock(buildDirectory);
        if (!IsComplete(publishDirectory))
        {
            IList<string> cachedArguments = GetPublishArguments(targetType, configuration, runtimeIdentifier, publishDirectory);
            foreach (string argument in GetInstalledBuildArguments(buildDirectory))
            {
                cachedArguments.Add(argument);
            }
            BuildCommands.BuildSolution.RunBuild(Path.Combine(pluginDirectory, "Managed", "UnrealSharp", "Runtime"),
                configuration, publish: true, cachedArguments);
            WriteManifest(publishDirectory);
        }
        foreach (string file in Directory.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(outputDirectory, Path.GetRelativePath(publishDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
        LoggerUtilities.LogUnrealSharpInfo($"Using cached UnrealSharp game runtime: {publishDirectory}");
    }

    private static IList<string> GetInstalledBuildArguments(string buildDirectory) =>
    [
        "-p:UsePrecompiledUnrealSharpTools=true",
        $"-p:UnrealSharpBuildRoot={buildDirectory}",
        $"-p:UnrealSharpRuntimeDirectory={Path.Combine(buildDirectory, "bin", DotNetUtilities.Version)}"
    ];

    private static FileStream AcquireBuildLock(string buildDirectory)
    {
        Directory.CreateDirectory(buildDirectory);
        string lockPath = Path.Combine(buildDirectory, ".build.lock");
        for (int attempt = 0; ; ++attempt)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 300)
            {
                if (attempt == 0)
                {
                    LoggerUtilities.LogUnrealSharpInfo("Waiting for another UnrealSharp runtime build to finish...");
                }
                Thread.Sleep(1000);
            }
        }
    }

    private static bool IsComplete(string outputDirectory)
    {
        string manifestPath = Path.Combine(outputDirectory, ManifestName);
        if (!File.Exists(manifestPath))
        {
            return false;
        }
        try
        {
            Dictionary<string, string>? files = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath));
            return files != null && files.ContainsKey("UnrealSharp.dll") && files.ContainsKey("UnrealSharp.Plugins.dll")
                && files.All(file => File.Exists(Path.Combine(outputDirectory, file.Key))
                    && GetHash(Path.Combine(outputDirectory, file.Key)) == file.Value);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void WriteManifest(string outputDirectory)
    {
        Dictionary<string, string> files = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file) != ManifestName)
            .ToDictionary(file => Path.GetRelativePath(outputDirectory, file), GetHash);
        File.WriteAllText(Path.Combine(outputDirectory, ManifestName), JsonSerializer.Serialize(files));
    }

    private static string GetHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
