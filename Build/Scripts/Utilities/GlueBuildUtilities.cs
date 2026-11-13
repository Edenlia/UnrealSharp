using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnrealBuildTool;
using UnrealSharp.Automation.BuildCommands;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.Utilities;

/// <summary>Builds framework and glue from the selected UHT target's exact project set.</summary>
public static class GlueBuildUtilities
{
    public static string WriteSolution(string directory, IEnumerable<string> projects, string solutionName)
    {
        string inputs = string.Join("\n", projects.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal));
        string stamp = Path.Combine(directory, solutionName + ".sln.projects");
        if (!File.Exists(Path.Combine(directory, solutionName + ".sln")) || !File.Exists(stamp) || File.ReadAllText(stamp) != inputs)
        {
            GenerateSolution.GenerateBuildSolution(directory, solutionName, inputs.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            GlueTarget.WriteIfChanged(stamp, inputs);
        }
        // Retire only the previous generated format, so dotnet selects the .sln unambiguously.
        File.Delete(Path.Combine(directory, "UnrealSharp.slnx"));
        return directory;
    }

    public static void BuildFramework(GlueTarget target, bool publish = false, IList<string>? extraArguments = null)
    {
        string managed = Path.Combine(target.PluginDirectory, "Managed", "UnrealSharp");
        List<string> projects = Directory.GetFiles(managed, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains(".Tests", StringComparison.OrdinalIgnoreCase) && (target.Type == "Editor" || !ProjectUtilities.IsEditorOnlyProject(path))).ToList();
        string solution = WriteSolution(Path.Combine(target.Root, "Build", "Framework"), projects, "UnrealSharp");
        List<string> arguments = GlueTargetUtilities.BuildArguments(target, framework: true);
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
        BuildEnginePluginBindings(target, output, extraArguments);
        BuildProjectBindings(target, output, extraArguments);
        EmitGlueLoadOrder(target, output);
    }

    public static void BuildEnginePluginBindings(GlueTarget target, string? outputDirectory = null, IList<string>? extraArguments = null)
    {
        BuildGlueProjects(target, target.Projects.Where(IsEnginePluginProject), "EnginePlugin", outputDirectory ?? target.OutputDirectory, extraArguments);
    }

    public static void BuildProjectBindings(GlueTarget target, string? outputDirectory = null, IList<string>? extraArguments = null)
    {
        BuildGlueProjects(target, target.Projects.Where(project => !IsEnginePluginProject(project)), "Project", outputDirectory ?? target.OutputDirectory, extraArguments);
    }

    private static bool IsEnginePluginProject(string project) =>
        string.Equals(Path.GetFileNameWithoutExtension(project), GlueTarget.MergedAssemblyName, StringComparison.OrdinalIgnoreCase);

    private static void BuildGlueProjects(GlueTarget target, IEnumerable<string> projectPaths, string buildFolder, string output, IList<string>? extraArguments)
    {
        string[] projects = projectPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (projects.Length == 0) return;
        CsProjectUtilities.UpdateUnrealSharpImports(() => Path.Combine(target.PluginDirectory, "UnrealSharp.Shared.props"), projects);
        string solutionName = buildFolder == "EnginePlugin" ? GlueTarget.MergedAssemblyName : "UnrealSharpGlue";
        string solution = WriteSolution(Path.Combine(target.Root, "Build", buildFolder), projects, solutionName);
        List<string> arguments = GlueTargetUtilities.BuildArguments(target);
        // Merged bindings can depend on independent glue projects outside this solution.
        if (buildFolder == "EnginePlugin")
            arguments.Add("-p:ShouldUnsetParentConfigurationAndPlatform=false");
        if (extraArguments != null) arguments.AddRange(extraArguments);
        arguments.Add($"-p:UnrealSharpUserAssemblyDirectory={output}");
        arguments.Add($"-p:PublishDir={output}");
        BuildSolution.RunBuild(solution, Enum.Parse<UnrealTargetConfiguration>(target.Configuration), publish: true, arguments);
    }

    public static void EmitGlueLoadOrder(GlueTarget target, string? outputDirectory = null)
    {
        string output = outputDirectory ?? target.OutputDirectory;
        if (target.Projects.Count == 0)
        {
            GlueTarget.WriteIfChanged(Path.Combine(output, "GlueCode.LoadOrder.json"), "{\"LoadOrder\":[],\"Collectible\":false,\"Priority\":100}");
            return;
        }
        LoadOrderUtilities.TryEmitLoadOrder(target.Projects, output, LoadOrderUtilities.GlueLoadOrderName,
            new LoadOrderOptions { Collectible = false, Priority = LoadOrderUtilities.GlueLoadOrderPriority }, requireAllAssemblies: true);
    }
}
