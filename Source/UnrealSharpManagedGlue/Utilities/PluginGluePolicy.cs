using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EpicGames.Core;
using EpicGames.UHT.Types;
using UnrealBuildTool;

namespace UnrealSharpManagedGlue.Utilities;

/// <summary>Plugin ownership and UnrealSharp's required (not all enabled) plugin closure.</summary>
public sealed class PluginGluePolicy
{
    private readonly Dictionary<string, PluginInfo> _owners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _required = new(StringComparer.OrdinalIgnoreCase);

    public PluginGluePolicy()
    {
        string? projectFile = GeneratorStatics.Factory.Session.ProjectFile;
        ProjectDescriptor? project = string.IsNullOrEmpty(projectFile) ? null : ProjectDescriptor.FromFile(new FileReference(projectFile));
        Dictionary<string, PluginInfo> plugins = Plugins.ToFilteredDictionary(Plugins.ReadAvailablePlugins(
                new DirectoryReference(GeneratorStatics.EngineDirectory),
                string.IsNullOrEmpty(projectFile) ? null : new FileReference(projectFile).Directory,
                project?.AdditionalPluginDirectories))
            .ToDictionary(pair => pair.Key, pair => pair.Value.ChoiceVersion!, StringComparer.OrdinalIgnoreCase);

        // BuildPlugin's explicitly packaged copy takes precedence over an engine-installed copy.
        FileReference unrealSharpFile = new(Path.Combine(GeneratorStatics.PluginDirectory, "UnrealSharp.uplugin"));
        PluginInfo unrealSharp = plugins.TryGetValue("UnrealSharp", out PluginInfo? discovered) && discovered.File == unrealSharpFile
            ? discovered : new PluginInfo(unrealSharpFile, PluginType.Project);
        plugins[unrealSharp.Name] = unrealSharp;

        // UBT may choose a version/project override or an explicit BuildPlugin copy.
        // Reconcile discovery with the directories of the modules UBT actually sent to UHT.
        foreach (UhtModule module in GeneratorStatics.Factory.Session.Modules)
        {
            DirectoryInfo? directory = new(module.Module.BaseDirectory);
            while (directory != null)
            {
                string? descriptor = directory.EnumerateFiles("*.uplugin", SearchOption.TopDirectoryOnly).Select(file => file.FullName).FirstOrDefault();
                if (descriptor != null)
                {
                    // ReadAvailablePlugins merges platform extensions into their
                    // parent. Preserve that ownership and the merged dependencies.
                    PluginInfo? parent = plugins.Values.FirstOrDefault(candidate => candidate.ChildFiles.Any(child =>
                        string.Equals(child.FullName, descriptor, StringComparison.OrdinalIgnoreCase)));
                    if (parent != null)
                    {
                        _owners[module.ShortName] = parent;
                        break;
                    }
                    string name = Path.GetFileNameWithoutExtension(descriptor);
                    if (!plugins.TryGetValue(name, out PluginInfo? plugin) || !string.Equals(plugin.File.FullName, descriptor, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = Path.GetRelativePath(GeneratorStatics.EngineDirectory, descriptor);
                        PluginType type = !Path.IsPathRooted(relative) && !relative.StartsWith("..", StringComparison.Ordinal) ? PluginType.Engine : PluginType.Project;
                        plugin = new PluginInfo(new FileReference(descriptor), type);
                        plugins[name] = plugin;
                    }
                    _owners[module.ShortName] = plugin;
                    break;
                }
                if (directory.FullName == GeneratorStatics.EngineDirectory) break;
                directory = directory.Parent;
            }
        }
        foreach (PluginInfo plugin in plugins.Values)
        {
            GeneratorStatics.Factory.AddExternalDependency(plugin.File.FullName);
            foreach (FileReference child in plugin.ChildFiles) GeneratorStatics.Factory.AddExternalDependency(child.FullName);
            if (plugin.Descriptor.Modules == null) continue;
            foreach (ModuleDescriptor module in plugin.Descriptor.Modules) _owners.TryAdd(module.Name, plugin);
        }

        Queue<PluginInfo> pending = new();
        pending.Enqueue(unrealSharp);
        while (pending.TryDequeue(out PluginInfo? plugin))
        {
            if (!_required.Add(plugin.Name) || plugin.Descriptor.Plugins == null) continue;
            GeneratorStatics.Factory.AddExternalDependency(plugin.File.FullName);
            foreach (PluginReferenceDescriptor reference in plugin.Descriptor.Plugins)
            {
                if (reference.bOptional || !reference.IsEnabledForPlatform(GeneratorStatics.TargetPlatform) ||
                    !reference.IsEnabledForTarget(GeneratorStatics.TargetType) ||
                    !reference.IsEnabledForTargetConfiguration(GeneratorStatics.TargetConfiguration)) continue;
                if (!plugins.TryGetValue(reference.Name, out PluginInfo? dependency))
                    throw new InvalidOperationException($"Required UnrealSharp plugin '{reference.Name}' was not found ({plugin.Name}).");
                if (dependency.Descriptor.SupportsTargetPlatform(GeneratorStatics.TargetPlatform)) pending.Enqueue(dependency);
            }
        }
    }

    public IEnumerable<string> NativeModuleNames => _owners.Keys;

    public PluginInfo? GetOwner(UhtPackage package) =>
        _owners.TryGetValue(package.GetModuleShortName(), out PluginInfo? owner) ? owner : null;

    public bool IsRequired(PluginInfo plugin) => _required.Contains(plugin.Name);
}
