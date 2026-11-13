using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnrealSharp.Automation.Utilities;
using UnrealSharp.Shared;
using UnrealSharpManagedGlue.Utilities;

namespace UnrealSharpManagedGlue;

public static class ModuleFactory
{
    public static void Reset()
    {
        ModuleDependencies.Clear();
        ModuleDependenciesChanged = false;
    }

    private static Dictionary<string, List<string>> ModuleDependencies = new();
    private static bool ModuleDependenciesChanged;

    internal static void Initialize()
    {
        ModuleDependencies = JsonUtilities.DeserializeObjectFromJson<Dictionary<string, List<string>>>(nameof(ModuleDependencies)) ?? new Dictionary<string, List<string>>();
        ModuleDependenciesChanged = false;
    }

    public static void SyncModuleProjects()
    {
        // Project Source and Project Plugin modules retain their own projects.
        foreach (ModuleInfo moduleInfo in ModuleUtilities.PackageToModuleInfo.Values.OrderBy(module => module.ModuleName, StringComparer.Ordinal))
        {
            if (!moduleInfo.Module.ShouldExportPackage() || moduleInfo.IsPartOfEngine || moduleInfo.IsMergedGlue || moduleInfo.IsExtendingModule)
                continue;
            bool recentlyCreatedModule = false;
            if (!File.Exists(moduleInfo.CsProjPath))
            {
                GenerateModuleProject(moduleInfo);
                recentlyCreatedModule = true;
            }

            List<string> pluginDependencies = moduleInfo.Extensions.Prepend(moduleInfo).SelectMany(module => module.Dependencies)
                .Where(dependency => !dependency.IsPartOfEngine && !dependency.IsExtendingModule)
                .Select(dependency => dependency.IsMergedGlue
                    ? Path.Combine(GeneratorStatics.Target.OutputDirectory, GlueTarget.MergedAssemblyName + ".dll")
                    : dependency.CsProjPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !string.Equals(path, moduleInfo.CsProjPath, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!recentlyCreatedModule && ModuleDependencies.TryGetValue(moduleInfo.ModuleName, out List<string>? existingDependencies) &&
                existingDependencies.OrderBy(path => path).SequenceEqual(pluginDependencies.OrderBy(path => path)))
                continue;

            UpdateModuleDependencies(moduleInfo, pluginDependencies);
            ModuleDependencies[moduleInfo.ModuleName] = pluginDependencies;
            ModuleDependenciesChanged = true;
        }
    }

    internal static void SaveModuleDependencies()
    {
        if (!ModuleDependenciesChanged) return;
        JsonUtilities.SerializeObjectToJson(ModuleDependencies, nameof(ModuleDependencies));
        ModuleDependenciesChanged = false;
    }

    private static void UpdateModuleDependencies(ModuleInfo moduleInfo, List<string> pluginDependencies)
    {
        // Migrate existing generated projects when their dependency cache changes.
        // Shared.props supplies the merged DLL reference from the selected output directory.
        XDocument project = XDocument.Load(moduleInfo.CsProjPath);
        project.Descendants("ProjectReference").Where(reference =>
            string.Equals(Path.GetFileNameWithoutExtension(((string?)reference.Attribute("Include") ?? "").Replace('\\', '/')),
                GlueTarget.MergedAssemblyName, StringComparison.OrdinalIgnoreCase)).Remove();
        project.Root!.Elements("PropertyGroup").Elements("UnrealSharpReferencesMergedBindings").Remove();
        if (pluginDependencies.Any(path => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
            project.Root.Elements("PropertyGroup").First().Add(new XElement("UnrealSharpReferencesMergedBindings", "true"));
        GlueTarget.WriteIfChanged(moduleInfo.CsProjPath, project.ToString());

        List<KeyValuePair<string, string>> arguments = new()
        {
            new("ProjectPath", moduleInfo.CsProjPath),
        };
        foreach (string path in pluginDependencies.Where(path => Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase)))
            arguments.Add(new("Dependencies", path));
        UnrealSharpAutomationUtilities.InvokeUnrealSharpAutomation("UpdateProjectDependencies", arguments);
    }

    internal static void PrepareEnginePluginProject(IEnumerable<ModuleInfo> modules)
    {
        ModuleInfo[] enginePlugins = modules.Where(module => module.IsMergedGlue).ToArray();
        if (enginePlugins.Length > 0) GenerateProject(enginePlugins[0].CsProjPath, enginePlugins);
    }

    private static void GenerateModuleProject(ModuleInfo moduleInfo) =>
        GenerateProject(moduleInfo.CsProjPath, new[] { moduleInfo });

    private static void GenerateProject(string projectPath, ModuleInfo[] modules)
    {
        GlueTarget target = GeneratorStatics.Target;
        string directory = Path.GetDirectoryName(projectPath)!;
        ModuleInfo[] dependencies = modules.SelectMany(module => module.Extensions.Prepend(module)).SelectMany(module => module.Dependencies)
            .Where(dependency => !dependency.IsPartOfEngine && !dependency.IsExtendingModule &&
                !string.Equals(dependency.CsProjPath, projectPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        XElement properties = new("PropertyGroup",
            new XElement("TargetFramework", target.Framework), new XElement("ImplicitUsings", "enable"),
            new XElement("Nullable", "enable"), new XElement("EnableDefaultCompileItems", "false"),
            new XElement("SkipIncludeAnalyzers", "true"), new XElement("UnrealSharpGeneratedProject", "true"),
            new XElement("UnrealSharpTargetProps", target.PropsPath));
        if (dependencies.Any(dependency => dependency.IsMergedGlue))
            properties.Add(new XElement("UnrealSharpReferencesMergedBindings", "true"));
        if (modules.All(module => module.Module.IsEditorOnly())) properties.Add(new XElement("IsEditorOnly", "true"));
        XElement compile = new("ItemGroup");
        HashSet<string> folders = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModuleInfo module in modules)
        {
            AddFolder(module.GlueOutputDirectory, "*.generated.cs");
            AddFolder(module.ExtensionsDirectory, "**/*.cs");
            foreach (ModuleInfo extension in module.Extensions)
            {
                AddFolder(extension.GlueOutputDirectory, "*.generated.cs");
                AddFolder(extension.ExtensionsDirectory, "**/*.cs");
                foreach (string folder in extension.Module.GetAdditionalExtensionFolders()) AddFolder(Path.Combine(extension.ScriptPath, folder), "**/*.cs");
            }
        }
        void AddFolder(string folder, string pattern)
        {
            string include = Path.Combine(Path.GetRelativePath(directory, folder), pattern).Replace('\\', '/');
            if (folders.Add(include)) compile.Add(new XElement("Compile", new XAttribute("Include", include), new XAttribute("Exclude", "**/obj/**;**/bin/**")));
        }
        XElement references = new("ItemGroup", dependencies.Where(dependency => !dependency.IsMergedGlue)
            .Select(dependency => dependency.CsProjPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new XElement("ProjectReference", new XAttribute("Include", Path.GetRelativePath(directory, path)))));
        XElement project = new("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties,
            new XElement("Import", new XAttribute("Project", Path.GetRelativePath(directory, Path.Combine(target.PluginDirectory, "UnrealSharp.Shared.props")))), compile, references);
        if (modules.Any(module => module.IsMergedGlue))
            project.Add(new XElement("ItemGroup", new XElement("Analyzer",
                new XAttribute("Include", "$(UnrealSharpAnalyzersDirectory)/UnrealSharp.ExtensionSourceGenerators.dll"))));
        GlueTarget.WriteIfChanged(projectPath, project.ToString());
    }

    internal static void ValidateProjectGraph(IEnumerable<ModuleInfo> modules)
    {
        Dictionary<string, HashSet<string>> graph = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModuleInfo module in modules.Where(module => !module.IsPartOfEngine && !module.IsExtendingModule))
        {
            if (!graph.TryGetValue(module.CsProjPath, out HashSet<string>? dependencies)) graph[module.CsProjPath] = dependencies = new(StringComparer.OrdinalIgnoreCase);
            foreach (ModuleInfo dependency in module.Extensions.Prepend(module).SelectMany(member => member.Dependencies))
                if (!dependency.IsPartOfEngine && !dependency.IsExtendingModule && dependency.CsProjPath != module.CsProjPath) dependencies.Add(dependency.CsProjPath);
        }
        HashSet<string> complete = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> active = new(StringComparer.OrdinalIgnoreCase);
        void Visit(string project)
        {
            if (complete.Contains(project)) return;
            if (!active.Add(project)) throw new InvalidOperationException($"Merging engine plugin glue creates a project reference cycle at '{project}'. Remove the cross-project dependency or merge the cycle into one assembly.");
            if (graph.TryGetValue(project, out HashSet<string>? dependencies)) foreach (string dependency in dependencies) Visit(dependency);
            active.Remove(project);
            complete.Add(project);
        }
        foreach (string project in graph.Keys) Visit(project);
    }
}
