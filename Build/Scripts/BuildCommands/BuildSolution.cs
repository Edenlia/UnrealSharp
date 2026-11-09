using System;
using System.Collections.Generic;
using System.IO;
using AutomationTool;
using UnrealBuildTool;
using UnrealSharp.Automation.Processes;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharp.Automation.BuildCommands;

[Help("Builds a solution file using MSBuild. Can also publish the solution if the \"Publish\" parameter is set to true.")]
[Help("Folders=<Path>+<Path>", "One or more solution folders to build. At least one is required.")]
[Help("Publish", "If set, the solution is published instead of built.")]
[Help("TargetConfiguration=<Config>", "The build configuration (Debug, DebugGame, Development, Shipping, etc.).")]
[Help("ExtraArguments=<Arg>+<Arg>", "Additional arguments forwarded to dotnet build/publish.")]
public class BuildSolution : BuildCommand
{
    public override void ExecuteBuild()
    {
        string[] Folders = ParseParamValues("Folders");

        if (Folders.Length == 0)
        {
            throw new AutomationException("At least one solution folder must be specified via -Folders=<Path>.");
        }

        bool Publish = ParseParam("Publish");
        UnrealTargetConfiguration TargetConfiguration = ParseRequiredEnumParamEnum<UnrealTargetConfiguration>("TargetConfiguration");
        string[] ExtraArguments = ParseParamValues("ExtraArguments");

        RunBuild(Folders, TargetConfiguration, Publish, ExtraArguments);
    }

    public static void RunBuild(string folder, UnrealTargetConfiguration buildConfig, bool publish, IList<string>? extraArguments = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        RunBuild([folder], buildConfig, publish, extraArguments, environment);
    }

    public static void RunBuild(IEnumerable<string> folders, UnrealTargetConfiguration buildConfig, bool publish, IList<string>? extraArguments = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(folders);

        List<string> FolderList = new List<string>(folders);

        if (FolderList.Count == 0)
        {
            throw new ArgumentException("At least one folder must be provided.", nameof(folders));
        }

        ValidateSolutionFolders(FolderList);

        string Action = publish ? "publish" : "build";
        string ConfigurationName = buildConfig.GetDotNetBuildConfiguration();

        foreach (string SolutionFolder in FolderList)
        {
            RunSingleBuild(SolutionFolder, Action, ConfigurationName, extraArguments, environment);
        }
    }

    private static void ValidateSolutionFolders(List<string> folders)
    {
        foreach (string SolutionFolder in folders)
        {
            if (string.IsNullOrWhiteSpace(SolutionFolder))
            {
                throw new ArgumentException("Solution folder paths cannot be null or empty.", nameof(folders));
            }

            if (!Directory.Exists(SolutionFolder))
            {
                throw new DirectoryNotFoundException($"Specified solution folder does not exist: {SolutionFolder}");
            }
        }
    }

    private static void RunSingleBuild(string solutionFolder, string action, string configurationName, IList<string>? extraArguments,
        IReadOnlyDictionary<string, string>? environment)
    {
        LoggerUtilities.LogUnrealSharpInfo($"Running dotnet {action} on {solutionFolder} (configuration: {configurationName}).");

        using DotnetProcess BuildSolutionProcess = new DotnetProcess();
        if (environment != null)
        {
            foreach ((string Name, string Value) in environment)
            {
                BuildSolutionProcess.StartInfo.Environment[Name] = Value;
            }
        }
        BuildSolutionProcess.StartInfo.ArgumentList.Add(action);
        BuildSolutionProcess.StartInfo.ArgumentList.Add(solutionFolder);
        BuildSolutionProcess.StartInfo.ArgumentList.Add("--configuration");
        BuildSolutionProcess.StartInfo.ArgumentList.Add(configurationName);

        if (extraArguments != null)
        {
            foreach (string ExtraArgument in extraArguments)
            {
                BuildSolutionProcess.StartInfo.ArgumentList.Add(ExtraArgument);
            }
        }

        BuildSolutionProcess.StartProcess();
    }
}
