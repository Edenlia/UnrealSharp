using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnrealSharp.Shared;

/// <summary>The exact UHT input/output context. Never infer membership from old generated directories.</summary>
public sealed class GlueTarget
{
    public const int CurrentVersion = 5;
    public const string MergedAssemblyName = "_MergedEnginePlugin";
    public const string FileName = "UnrealSharp.Target.json";
    public int Version { get; set; } = CurrentVersion;
    public string IntermediateDirectory { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Configuration { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string PluginDirectory { get; set; } = "";
    public string ProjectDirectory { get; set; } = "";
    public string ProjectFile { get; set; } = "";
    public string Framework { get; set; } = "net10.0";
    public bool BuildAllModules { get; set; }
    public bool Distribution { get; set; }
    public bool Precompiled { get; set; }
    public List<string> Projects { get; set; } = new();
    public Dictionary<string, string> ModuleAssemblies { get; set; } = new();
    public List<string> NativeModules { get; set; } = new();

    [JsonIgnore] public bool IsDistributionBuild => Distribution && !Precompiled;
    [JsonIgnore] public string Root => IntermediateDirectory;
    [JsonIgnore] public string EngineGlue => Precompiled ? Path.Combine(BindingsRoot, "Source")
        : IsDistributionBuild || string.IsNullOrEmpty(ProjectFile) ? Path.Combine(Root, "Engine")
        : Path.Combine(PluginDirectory, "Intermediate", "UnrealSharp", "UHT", Type);
    [JsonIgnore] public string BindingsRoot => Distribution || Precompiled
        ? Path.Combine(PluginDirectory, "Binaries", "Managed", "BuildPlugin", Platform, Architecture, Name, Configuration)
        : Path.Combine(PluginDirectory, "Binaries", "Managed");
    [JsonIgnore] public string BindingsDirectory => Path.Combine(BindingsRoot, Framework);
    [JsonIgnore] public string BundleDirectory => Path.Combine(BindingsRoot, "Glue", Framework);
    [JsonIgnore] public string OutputRoot => IsDistributionBuild ? BindingsRoot
        : Path.Combine(string.IsNullOrEmpty(ProjectFile) ? PluginDirectory : ProjectDirectory, "Binaries", "Managed");
    [JsonIgnore] public string OutputDirectory => Path.Combine(OutputRoot, Framework);
    [JsonIgnore] public string ManifestPath => Path.Combine(Root, FileName);
    [JsonIgnore] public string PropsPath => Path.Combine(Root, "UnrealSharp.Target.props");

    public void Save()
    {
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        WriteIfChanged(ManifestPath, json);
        // Keep the existing per-type entry point for UAT and the Editor. It is
        // an active-context index; generated files live at IntermediateDirectory.
        if (!IsDistributionBuild && !string.IsNullOrEmpty(ProjectFile))
            WriteIfChanged(GetProjectManifestPath(ProjectDirectory, Type), json);
    }

    private static string GetProjectManifestPath(string projectDirectory, string type) =>
        Path.Combine(projectDirectory, "Intermediate", "UnrealSharp", "UHT", type, FileName);

    public static GlueTarget Read(string path)
    {
        GlueTarget target = JsonSerializer.Deserialize<GlueTarget>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Invalid UnrealSharp target manifest: {path}");
        // A distribution may have been generated on another OS. Its build-machine
        // Intermediate path is replaced when mounted, never used by the consumer.
        if (target.Version != CurrentVersion || (!target.Distribution && !Path.IsPathFullyQualified(target.IntermediateDirectory)))
            throw new InvalidDataException($"Unsupported UnrealSharp target manifest: {path}");
        return target;
    }

    public static GlueTarget Select(string projectDirectory, string type, string platform, string configuration,
        string? name = null, string? architecture = null)
    {
        return SelectProject(projectDirectory, type, platform, configuration, name, architecture)
            ?? throw new InvalidOperationException($"No UnrealSharp UHT target for {projectDirectory} / {type} / {platform} / {configuration}. Build the native target first.");
    }

    public static GlueTarget? SelectProject(string projectDirectory, string type, string platform, string configuration,
        string? name = null, string? architecture = null)
    {
        string manifest = GetProjectManifestPath(projectDirectory, type);
        if (!File.Exists(manifest)) return null;
        using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(manifest)))
            if (!json.RootElement.TryGetProperty("Version", out JsonElement version) || version.GetInt32() != CurrentVersion) return null;
        GlueTarget target = Read(manifest);
        if (target.IsDistributionBuild || target.Type != type || target.Platform != platform ||
            (target.Precompiled && target.Configuration != configuration) ||
            (!string.IsNullOrEmpty(name) && target.Name != name) ||
            (!string.IsNullOrEmpty(architecture) && !target.MatchesArchitecture(architecture))) return null;
        // Recompiling source for packaging must not change the native generation
        // directory. A precompiled bundle must match the requested configuration.
        target.Configuration = configuration;
        return target;
    }

    public static GlueTarget? SelectFrom(string directory, string type, string platform, string configuration,
        string? name = null, string? architecture = null, bool distributionOnly = false)
    {
        List<GlueTarget> matches = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, FileName, SearchOption.AllDirectories)
                .Where(path => { using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
                    return json.RootElement.TryGetProperty("Version", out JsonElement version) && version.GetInt32() == CurrentVersion; })
                .Select(Read).Where(target =>
                (!distributionOnly || target.Distribution) &&
                string.Equals(target.Type, type, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(target.Platform, platform, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(target.Configuration, configuration, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(name) || string.Equals(target.Name, name, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(architecture) || target.MatchesArchitecture(architecture))).ToList()
            : new List<GlueTarget>();
        if (matches.Count > 1)
            throw new InvalidOperationException($"Multiple UnrealSharp targets for {directory} / {name} / {type} / {platform} / {configuration} / {architecture}. Select -TargetName and -TargetArchitecture explicitly.");
        return matches.SingleOrDefault();
    }

    public bool MatchesArchitecture(string architecture) =>
        string.Equals(Architecture, architecture, StringComparison.OrdinalIgnoreCase) ||
        Architecture.Split('+').Contains(architecture, StringComparer.OrdinalIgnoreCase);

    public void MountDistribution(string pluginDirectory, string projectFile)
    {
        // Resolve the readable package layout relative to its installed location.
        PluginDirectory = Path.GetFullPath(pluginDirectory);
        ProjectFile = Path.GetFullPath(projectFile);
        ProjectDirectory = Path.GetDirectoryName(ProjectFile)!;
        IntermediateDirectory = Path.Combine(ProjectDirectory, "Intermediate", "UnrealSharp", "UHT", Type);
        Precompiled = true;
        Projects = Projects.Select(path => Path.GetFileName(path.Replace('\\', '/'))).ToList();
    }

    public void CopyBundle(string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string assembly in Projects.Select(path => Path.GetFileNameWithoutExtension(path)))
        {
            string dll = Path.Combine(BundleDirectory, assembly + ".dll");
            if (!File.Exists(dll)) throw new FileNotFoundException("Packaged UnrealSharp glue is missing", dll);
            foreach (string suffix in new[] { ".dll", ".pdb", ".deps.json" })
            {
                string source = Path.Combine(BundleDirectory, assembly + suffix);
                if (File.Exists(source)) File.Copy(source, Path.Combine(destination, Path.GetFileName(source)), true);
            }
        }
        File.Copy(Path.Combine(BundleDirectory, "GlueCode.LoadOrder.json"), Path.Combine(destination, "GlueCode.LoadOrder.json"), true);
    }

    public static void WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && File.ReadAllText(path) == text) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
