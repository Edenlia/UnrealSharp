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
    public static void SyncModuleProjects()
    {
        GlueTarget target = GeneratorStatics.Target;
        ModuleInfo[] modules = ModuleUtilities.PackageToModuleInfo.Values.OrderBy(module => module.ModuleName, StringComparer.Ordinal).ToArray();
        target.Projects.Clear();
        target.ModuleAssemblies.Clear();
        IEnumerable<string> previousModules = File.Exists(target.ManifestPath) ? GlueTarget.Read(target.ManifestPath).NativeModules : Array.Empty<string>();
        target.NativeModules = GeneratorStatics.PluginPolicy.NativeModuleNames.Concat(modules.Select(module => module.ModuleName))
            .Concat(previousModules)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.Ordinal).ToList();
        foreach (ModuleInfo module in modules.Where(module => !module.IsExtendingModule))
            target.ModuleAssemblies.Add(module.ModuleName, module.IsPartOfEngine ? "UnrealSharp" : Path.GetFileNameWithoutExtension(module.CsProjPath));

        // Membership comes from this session, never from abandoned module directories.
        XElement engineItems = new("ItemGroup");
        foreach (ModuleInfo module in modules.Where(module => module.IsPartOfEngine))
            engineItems.Add(new XElement("Compile", new XAttribute("Include", "$(MSBuildThisFileDirectory)" +
                Path.GetRelativePath(target.EngineGlue, module.GlueOutputDirectory).Replace('\\', '/') + "/*.generated.cs")));
        GlueTarget.WriteIfChanged(Path.Combine(target.EngineGlue, "UnrealSharp.EngineGlue.props"), new XElement("Project", engineItems).ToString());
        foreach (IGrouping<string, ModuleInfo> group in modules.Where(module => !module.IsPartOfEngine && !module.IsExtendingModule)
                     .GroupBy(module => module.CsProjPath, StringComparer.OrdinalIgnoreCase))
        {
            GenerateProject(group.Key, group.ToArray());
            target.Projects.Add(group.Key);
        }
        ValidateProjectGraph(modules);
        GlueTargetUtilities.WriteBuildProps(target);
        target.Save();
    }

    private static void GenerateProject(string projectPath, ModuleInfo[] modules)
    {
        GlueTarget target = GeneratorStatics.Target;
        string directory = Path.GetDirectoryName(projectPath)!;
        XElement properties = new("PropertyGroup",
            new XElement("TargetFramework", target.Framework), new XElement("ImplicitUsings", "enable"),
            new XElement("Nullable", "enable"), new XElement("EnableDefaultCompileItems", "false"),
            new XElement("SkipIncludeAnalyzers", "true"), new XElement("UnrealSharpGeneratedProject", "true"),
            new XElement("UnrealSharpTargetProps", target.PropsPath));
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
        XElement references = new("ItemGroup", modules.SelectMany(module => module.Extensions.Prepend(module)).SelectMany(module => module.Dependencies)
            .Where(dependency => !dependency.IsPartOfEngine && !dependency.IsExtendingModule)
            .Select(dependency => dependency.CsProjPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !string.Equals(path, projectPath, StringComparison.OrdinalIgnoreCase)).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new XElement("ProjectReference", new XAttribute("Include", Path.GetRelativePath(directory, path)))));
        XElement project = new("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties,
            new XElement("Import", new XAttribute("Project", Path.GetRelativePath(directory, Path.Combine(target.PluginDirectory, "UnrealSharp.Shared.props")))), compile, references);
        GlueTarget.WriteIfChanged(projectPath, project.ToString());
    }

    private static void ValidateProjectGraph(IEnumerable<ModuleInfo> modules)
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
