using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities;
using UnrealSharp.Shared;

namespace UnrealSharpManagedGlue.Utilities;

public static class BuildUtilities
{
    public static IEnumerable<string> GetFrameworkInputFiles()
    {
        return GetFrameworkInputDirectories().SelectMany(Directory.EnumerateFiles).Where(IsFrameworkInput)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal);
    }

    public static IEnumerable<string> GetFrameworkInputDirectories()
    {
        string plugin = GeneratorStatics.Target.PluginDirectory;
        string managed = Path.Combine(plugin, "Managed");
        return EnumerateFrameworkInputDirectories(Path.Combine(managed, "UnrealSharp"))
            .Concat(EnumerateFrameworkInputDirectories(Path.Combine(managed, "Shared")))
            .Concat(new[] { plugin, managed })
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateFrameworkInputDirectories(string directory)
    {
        if (!Directory.Exists(directory)) yield break;
        yield return directory;
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            string name = Path.GetFileName(child);
            // Build outputs and tests must not invalidate framework inputs or their directory membership.
            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(".", StringComparison.Ordinal)
                || name.Contains(".Tests", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string inputDirectory in EnumerateFrameworkInputDirectories(child))
                yield return inputDirectory;
        }
    }

    private static bool IsFrameworkInput(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".csproj" or ".props" or ".targets"
            or ".resx" or ".json" or ".config" or ".md"
        || Path.GetFileName(path).Equals(".editorconfig", StringComparison.OrdinalIgnoreCase);

    public static void PrepareBindings()
    {
        GlueTarget target = GeneratorStatics.Target;
        ModuleInfo[] modules = ModuleUtilities.PackageToModuleInfo.Values.OrderBy(module => module.ModuleName, StringComparer.Ordinal).ToArray();
        target.Projects.Clear();
        target.ModuleAssemblies.Clear();
        target.NativeModules = GeneratorStatics.PluginPolicy.NativeModuleNames.Concat(modules.Select(module => module.ModuleName))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.Ordinal).ToList();
        foreach (ModuleInfo module in modules.Where(module => !module.IsExtendingModule))
            target.ModuleAssemblies.Add(module.ModuleName, module.AssemblyName);
        target.Projects.AddRange(modules.Where(module => !module.IsPartOfEngine && !module.IsExtendingModule)
            .Select(module => module.CsProjPath).Distinct(StringComparer.OrdinalIgnoreCase));
        ModuleFactory.ValidateProjectGraph(modules);

        // Fixed and merged build inputs are prepared independently of project modules.
        XElement engineItems = new("ItemGroup");
        foreach (ModuleInfo module in modules.Where(module => module.IsPartOfEngine))
            engineItems.Add(new XElement("Compile", new XAttribute("Include", "$(MSBuildThisFileDirectory)" +
                Path.GetRelativePath(target.EngineGlue, module.GlueOutputDirectory).Replace('\\', '/') + "/*.generated.cs")));
        GlueTarget.WriteIfChanged(Path.Combine(target.EngineGlue, "UnrealSharp.EngineGlue.props"), new XElement("Project", engineItems).ToString());
        ModuleFactory.PrepareEnginePluginProject(modules);
        GlueTargetUtilities.WriteBuildProps(target);
        target.Save();
    }

    public static void BuildUnrealSharpBindings()
    {
        if (!ShouldCompileBindings) return;
        GlueTarget target = GeneratorStatics.Target;
        BuildIfNeeded("Framework", FileExporter.HasModifiedEngineGlue,
            new[] { Path.Combine(target.BindingsDirectory, "UnrealSharp.dll") },
            () => GetBuildInputs(Array.Empty<string>(), framework: true),
            () => GlueBuildUtilities.BuildFramework(target));
    }

    public static void BuildEnginePluginBindings()
    {
        if (!ShouldCompileBindings) return;
        GlueTarget target = GeneratorStatics.Target;
        string[] projects = target.Projects.Where(IsEnginePluginProject).ToArray();
        BuildGlueIfNeeded("EnginePlugin", projects, () => GlueBuildUtilities.BuildEnginePluginBindings(target));
    }

    public static void BuildProjectBindings()
    {
        if (!ShouldCompileBindings) return;
        GlueTarget target = GeneratorStatics.Target;
        string[] projects = target.Projects.Where(project => !IsEnginePluginProject(project)).ToArray();
        BuildGlueIfNeeded("Project", projects, () => GlueBuildUtilities.BuildProjectBindings(target));
        ModuleFactory.SaveModuleDependencies();
    }

    private static bool IsEnginePluginProject(string project) =>
        string.Equals(Path.GetFileNameWithoutExtension(project), GlueTarget.MergedAssemblyName, StringComparison.OrdinalIgnoreCase);

    private static void BuildGlueIfNeeded(string stage, string[] projects, Action build)
    {
        if (projects.Length == 0) return;
        HashSet<string> assemblies = projects.Select(project => Path.GetFileNameWithoutExtension(project)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool dirty = ModuleUtilities.PackageToModuleInfo.Values.Any(module => module.IsDirty && assemblies.Contains(module.AssemblyName));
        string[] outputs = assemblies.Select(assembly => Path.Combine(GeneratorStatics.Target.OutputDirectory, assembly + ".dll")).ToArray();
        BuildIfNeeded(stage, dirty, outputs, () => GetBuildInputs(projects, includeMerged: stage == "Project"), build);
    }

    private static void BuildIfNeeded(string stage, bool dirty, string[] outputs, Func<string> getInputs, Action build)
    {
        string stamp = Path.Combine(GeneratorStatics.Target.Root, stage + ".Bindings.inputs");
        string inputs = getInputs();
        if (!dirty && outputs.All(File.Exists) && File.Exists(stamp) && File.ReadAllText(stamp) == inputs)
        {
            ConsoleUtilities.Log($"{stage} bindings are unchanged. Skipping managed build.");
            return;
        }

        // A failed build must be retried even when UHT has already saved its generated sources.
        File.Delete(stamp);
        build();
        GlueTarget.WriteIfChanged(stamp, getInputs());
    }

    private static string GetBuildInputs(string[] projects, bool framework = false, bool includeMerged = false)
    {
        GlueTarget target = GeneratorStatics.Target;
        List<string> files = new()
        {
            target.PropsPath, Path.Combine(target.EngineGlue, "UE5Rules.Defines.props"),
            Path.Combine(target.PluginDirectory, "UnrealSharp.Build.props"),
            Path.Combine(target.PluginDirectory, "UnrealSharp.AOT.props")
        };
        List<string> references = new();
        if (!framework)
        {
            files.AddRange(projects);
            files.Add(Path.Combine(target.PluginDirectory, "UnrealSharp.Shared.props"));
            files.Add(Path.Combine(target.Root, "UnrealSharp.References.targets"));
            if (Directory.Exists(target.BindingsDirectory))
                references.AddRange(Directory.EnumerateFiles(target.BindingsDirectory, "*.dll"));
            if (includeMerged && target.Projects.Any(IsEnginePluginProject))
                references.Add(Path.Combine(target.OutputDirectory, GlueTarget.MergedAssemblyName + ".dll"));
        }

        // Another project can rebuild the shared DLL without changing its bytes.
        // Compare reference contents so that rewrite does not invalidate local glue.
        // Fixed module membership changes continue to require a full rebuild.
        return new XElement("Inputs",
            files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new XElement("File", new XAttribute("Path", path),
                    new XAttribute("Exists", File.Exists(path)), File.Exists(path) ? File.ReadAllText(path) : string.Empty)),
            // Hand-written framework and generator changes do not necessarily change UHT glue.
            // Hash their contents instead of embedding all managed source in the stamp.
            framework ? GetFrameworkInputFiles().Select(path =>
            {
                using FileStream stream = File.OpenRead(path);
                return new XElement("FrameworkInput", new XAttribute("Path", path),
                    new XAttribute("SHA256", Convert.ToHexString(SHA256.HashData(stream))));
            }) : null,
            references.OrderBy(path => path, StringComparer.Ordinal).Select(path =>
            {
                FileInfo file = new(path);
                string hash = string.Empty;
                if (file.Exists)
                {
                    using FileStream stream = file.OpenRead();
                    hash = Convert.ToHexString(SHA256.HashData(stream));
                }
                return new XElement("Reference", new XAttribute("Path", path), new XAttribute("Exists", file.Exists),
                    new XAttribute("SHA256", hash));
            })).ToString();
    }

    public static void PublishRuntimeContext()
    {
        if (!ShouldCompileBindings) return;
        // Publish the complete load list only after both glue build phases succeed.
        GlueBuildUtilities.EmitGlueLoadOrder(GeneratorStatics.Target);
        GlueTargetUtilities.PublishRuntimeContext(GeneratorStatics.Target);
    }

    // Ordinary non-Editor UHT generates source; PackageProject compiles it into
    // the archive. BuildPlugin also compiles its non-Editor distribution bundles.
    private static bool ShouldCompileBindings =>
        GeneratorStatics.Target.IsDistributionBuild || GeneratorStatics.TargetType == TargetType.Editor;

    public static void GenerateUserSolution()
    {
        if (GeneratorStatics.TargetType != TargetType.Editor || GeneratorStatics.Target.Distribution || string.IsNullOrEmpty(GeneratorStatics.Target.ProjectFile)) return;
        List<KeyValuePair<string, string>> arguments = new()
        {
            new("TargetType", GeneratorStatics.TargetType.ToString()), new("TargetConfiguration", GeneratorStatics.TargetConfiguration.ToString()),
            new("TargetPlatform", GeneratorStatics.TargetPlatform.ToString()), new("UnrealSharpTargetManifest", GeneratorStatics.Target.ManifestPath)
        };
        UnrealSharpAutomationUtilities.InvokeUnrealSharpAutomation("GenerateUserSolution", arguments);
    }
}
