using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using AutomationTool;
using UnrealBuildTool;
using UnrealSharp.Automation.BuildCommands;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.Utilities;

public static class GlueTargetUtilities
{
    public static GlueTarget Resolve(BuildCommand command)
    {
        string? manifest = command.ParseParamValue("UnrealSharpTargetManifest", null);
        if (!string.IsNullOrEmpty(manifest))
        {
            GlueTarget explicitTarget = GlueTarget.Read(manifest);
            ValidateSelection(command, explicitTarget);
            if (explicitTarget.Distribution &&
                (!string.Equals(explicitTarget.ProjectDirectory, command.GetProjectRootFolder(), StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(explicitTarget.PluginDirectory, command.GetUnrealSharpRootFolder(), StringComparison.OrdinalIgnoreCase)))
                MountDistribution(command, explicitTarget);
            return explicitTarget;
        }
        ManagedBuildTarget target = ManagedBuildTarget.FromCommand(command);
        string? name = command.ParseParamValue("TargetName", null);
        string? architecture = command.ParseParamValue("TargetArchitecture", null);
        string projectRoot = command.GetProjectRootFolder();
        GlueTarget? selected = GlueTarget.SelectProject(projectRoot,
            target.Type.ToString(), target.Platform.ToString(), target.Configuration.ToString(), name, architecture);
        if (selected != null)
        {
            WriteBuildProps(selected);
            return selected;
        }
        string plugin = command.GetUnrealSharpRootFolder();
        selected = GlueTarget.SelectFrom(Path.Combine(plugin, "Binaries", "Managed", "BuildPlugin"),
            target.Type.ToString(), target.Platform.ToString(), target.Configuration.ToString(), name, architecture, distributionOnly: true)
            ?? throw new InvalidOperationException($"No UnrealSharp glue for {projectRoot} / {target.Type} / {target.Platform} / {target.Configuration}. Build the native target first.");
        MountDistribution(command, selected);
        return selected;
    }

    private static void MountDistribution(BuildCommand command, GlueTarget target)
    {
        target.MountDistribution(command.GetUnrealSharpRootFolder(), command.GetUProjectFile().FullName);
        target.Save();
        WriteBuildProps(target);
        target.CopyBundle(target.OutputDirectory);
        PublishRuntimeContext(target);
    }

    private static void ValidateSelection(BuildCommand command, GlueTarget target)
    {
        void Check(string field, string? requested, string actual)
        {
            if (!string.IsNullOrEmpty(requested) && !string.Equals(requested, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"UnrealSharp manifest {field} is {actual}, but the command requested {requested}.");
        }
        Check("type", command.ParseParamValue("TargetType", command.ParseParamValue("UETargetType", null)), target.Type);
        Check("configuration", command.ParseParamValue("TargetConfiguration", command.ParseParamValue("UEBuildConfig", null)), target.Configuration);
        Check("platform", command.ParseParamValue("TargetPlatform", null), target.Platform);
        Check("name", command.ParseParamValue("TargetName", null), target.Name);
        string? architecture = command.ParseParamValue("TargetArchitecture", null);
        if (!string.IsNullOrEmpty(architecture) && !target.MatchesArchitecture(architecture))
            throw new InvalidOperationException($"UnrealSharp manifest architecture is {target.Architecture}, but the command requested {architecture}.");
    }

    public static List<string> BuildArguments(GlueTarget target, bool framework = false)
    {
        List<string> arguments = new()
        {
            $"-p:UnrealSharpTargetProps={target.PropsPath}",
            $"-p:UnrealSharpBuildingFramework={framework.ToString().ToLowerInvariant()}",
            $"-p:UETargetType={target.Type}", $"-p:UEBuildConfig={target.Configuration}",
            $"-p:UnrealSharpEngineGlue={target.EngineGlue}",
            $"-p:UnrealSharpBindingsRoot={target.BindingsRoot}"
        };
        if (target.IsDistributionBuild)
            arguments.Add($"-p:CustomBeforeDirectoryBuildProps={Path.Combine(target.PluginDirectory, "UnrealSharp.Target.props")}");
        return arguments;
    }

    public static void WriteBuildProps(GlueTarget target)
    {
        XElement properties = new("PropertyGroup",
            new XElement("UnrealSharpTargetRoot", target.Root),
            new XElement("UnrealSharpEngineGlue", target.EngineGlue), new XElement("UnrealSharpBindingsRoot", target.BindingsRoot),
            new XElement("UnrealSharpBindingsDirectory", target.BindingsDirectory),
            new XElement("UnrealSharpUserAssemblyDirectory", target.OutputDirectory),
            new XElement("UETargetType", target.Type), new XElement("UEBuildConfig", target.Configuration));
        GlueTarget.WriteIfChanged(target.PropsPath, new XElement("Project", properties).ToString());
        XElement items = new("ItemGroup", new XElement("Reference", new XAttribute("Remove", string.Join(";", target.NativeModules.Append(GlueTarget.MergedAssemblyName)))));
        foreach (string project in target.Projects)
            items.Add(new XElement("Reference", new XAttribute("Include", Path.GetFileNameWithoutExtension(project)),
                new XElement("HintPath", "$(UnrealSharpUserAssemblyDirectory)/" + Path.GetFileNameWithoutExtension(project) + ".dll"), new XElement("Private", "true")));
        XElement references = new("Project", new XElement("Target", new XAttribute("Name", "ResolveUnrealSharpTargetGlue"),
            new XAttribute("BeforeTargets", "ResolveAssemblyReferences"),
            new XAttribute("Condition", "'$(UnrealSharpGeneratedProject)' != 'true' and '$(UnrealSharpBuildingFramework)' != 'true'"), items));
        GlueTarget.WriteIfChanged(Path.Combine(target.Root, "UnrealSharp.References.targets"), references.ToString());
    }

    public static string WriteSolution(string directory, IEnumerable<string> projects)
    {
        XElement solution = new("Solution", projects.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new XElement("Project", new XAttribute("Path", Path.GetRelativePath(directory, path).Replace('\\', '/')))));
        GlueTarget.WriteIfChanged(Path.Combine(directory, "UnrealSharp.slnx"), solution.ToString());
        return directory;
    }

    public static void BuildFramework(GlueTarget target, bool publish = false, IList<string>? extraArguments = null)
    {
        string managed = Path.Combine(target.PluginDirectory, "Managed", "UnrealSharp");
        List<string> projects = Directory.GetFiles(managed, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains(".Tests", StringComparison.OrdinalIgnoreCase) && (target.Type == "Editor" || !ProjectUtilities.IsEditorOnlyProject(path))).ToList();
        string solution = WriteSolution(Path.Combine(target.Root, "Build", "Framework"), projects);
        List<string> arguments = BuildArguments(target, framework: true);
        if (extraArguments != null) arguments.AddRange(extraArguments);
        BuildSolution.RunBuild(solution, Enum.Parse<UnrealTargetConfiguration>(target.Configuration), publish, arguments);
    }

    public static void BuildGlue(GlueTarget target, string? outputDirectory = null, IList<string>? extraArguments = null)
    {
        string output = outputDirectory ?? target.OutputDirectory;
        if (target.Precompiled)
        {
            target.CopyBundle(output);
            return;
        }
        if (target.Projects.Count == 0)
        {
            GlueTarget.WriteIfChanged(Path.Combine(output, "GlueCode.LoadOrder.json"), "{\"LoadOrder\":[],\"Collectible\":false,\"Priority\":100}");
            return;
        }
        string solution = WriteSolution(Path.Combine(target.Root, "Build", "Glue"), target.Projects);
        List<string> arguments = BuildArguments(target);
        if (extraArguments != null) arguments.AddRange(extraArguments);
        arguments.Add($"-p:PublishDir={output}");
        BuildSolution.RunBuild(solution, Enum.Parse<UnrealTargetConfiguration>(target.Configuration), publish: true, arguments);
        LoadOrderUtilities.TryEmitLoadOrder(target.Projects, output, LoadOrderUtilities.GlueLoadOrderName,
            new LoadOrderOptions { Collectible = false, Priority = LoadOrderUtilities.GlueLoadOrderPriority }, requireAllAssemblies: true);
    }

    public static void PublishRuntimeContext(GlueTarget target)
    {
        // Runtime uses conventional relative paths, not build-time absolute paths.
        string json = JsonSerializer.Serialize(target, new JsonSerializerOptions { WriteIndented = true });
        GlueTarget.WriteIfChanged(Path.Combine(target.OutputRoot, GlueTarget.FileName), json);
        GlueTarget.WriteIfChanged(Path.Combine(target.OutputRoot, "UnrealSharp.Target.props"), File.ReadAllText(target.PropsPath));
        if (target.IsDistributionBuild)
        {
            Directory.CreateDirectory(target.BundleDirectory);
            foreach (string project in target.Projects)
                foreach (string suffix in new[] { ".dll", ".pdb", ".deps.json" })
                {
                    string source = Path.Combine(target.OutputDirectory, Path.GetFileNameWithoutExtension(project) + suffix);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(target.BundleDirectory, Path.GetFileName(source)), true);
                }
            File.Copy(Path.Combine(target.OutputDirectory, "GlueCode.LoadOrder.json"), Path.Combine(target.BundleDirectory, "GlueCode.LoadOrder.json"), true);
            // BuildPlugin removes HostProject. Retain the exact target's fixed
            // sources beside its binaries so managed packaging can recompile them.
            string sourceDirectory = Path.Combine(target.BindingsRoot, "Source");
            foreach (string source in Directory.EnumerateFiles(target.EngineGlue, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path) is ".cs" or ".props"))
            {
                string destination = Path.Combine(sourceDirectory, Path.GetRelativePath(target.EngineGlue, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
            WriteDistributionFilter(target);
        }
    }

    private static void WriteDistributionFilter(GlueTarget target)
    {
        // BuildPlugin copies Binaries from the source plugin but deletes HostProject
        // afterwards. Include only contexts produced for this temporary plugin copy.
        string[] manifests = Directory.GetFiles(Path.Combine(target.PluginDirectory, "Binaries", "Managed", "BuildPlugin"), GlueTarget.FileName, SearchOption.AllDirectories);
        var directories = manifests.Where(path =>
            {
                using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
                return json.RootElement.TryGetProperty("Version", out JsonElement version) && version.GetInt32() == GlueTarget.CurrentVersion;
            }).Select(GlueTarget.Read).Where(candidate => candidate.Distribution &&
            string.Equals(candidate.PluginDirectory, target.PluginDirectory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.ProjectDirectory, target.ProjectDirectory, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => Path.GetRelativePath(target.PluginDirectory, candidate.BindingsRoot).Replace('\\', '/'))
            .Distinct().OrderBy(directory => directory, StringComparer.Ordinal);
        const string begin = "; BEGIN UnrealSharp distribution targets";
        const string end = "; END UnrealSharp distribution targets";
        string path = Path.Combine(target.PluginDirectory, "Config", "FilterPlugin.ini");
        string filter = File.ReadAllText(path);
        int start = filter.IndexOf(begin, StringComparison.Ordinal);
        if (start >= 0)
        {
            int finish = filter.IndexOf(end, start, StringComparison.Ordinal);
            if (finish < 0) throw new InvalidDataException("Unterminated UnrealSharp distribution filter");
            filter = filter.Remove(start, finish + end.Length - start).TrimEnd();
        }
        string rules = begin + "\n-/Binaries/Managed/...\n-/Intermediate/UnrealSharp/...\n";
        foreach (string directory in directories)
            rules += $"/{directory}/...\n";
        rules += "-/Binaries/Managed/.../BuildHost-net472/...\n-/Binaries/Managed/.../UnrealSharp.GlueGenerator.Tests.*\n" + end + "\n";
        GlueTarget.WriteIfChanged(path, filter.TrimEnd() + "\n\n" + rules);
    }
}
