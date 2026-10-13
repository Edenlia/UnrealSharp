using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EpicGames.Core;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharpManagedGlue.Utilities;

public static class BuildUtilities
{
    public static void StageAutomationModule()
    {
        if (GeneratorStatics.TargetType != TargetRules.TargetType.Editor)
        {
            return;
        }

        // Supply a discovery record for packaged Automation commands. Leave existing
        // records and all compilation decisions to UE, including source dependency tracking.
        string pluginDirectory = GeneratorStatics.PluginDirectory;
        string projectPath = Path.Combine(pluginDirectory, "Build", "Scripts", "UnrealSharp.Automation.csproj");
        string assemblyPath = Path.Combine(pluginDirectory, "Binaries", "Managed", "Automation", "UnrealSharp.Automation.dll");
        string recordDirectory = Path.Combine(pluginDirectory, "Intermediate", "ScriptModules");
        string recordPath = Path.Combine(recordDirectory, "UnrealSharp.Automation.json");
        if (File.Exists(recordPath))
        {
            return;
        }

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("The UnrealSharp automation module was not staged by its build.", assemblyPath);
        }

        Directory.CreateDirectory(recordDirectory);

        CsProjBuildRecord record = new()
        {
            Version = CsProjBuildRecord.CurrentVersion,
            ProjectPath = Path.GetRelativePath(recordDirectory, projectPath),
            TargetPath = Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, assemblyPath),
            TargetBuildTime = File.GetLastWriteTime(assemblyPath)
        };
        File.WriteAllText(recordPath,
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void BuildBindings()
    {
        if (!GeneratorStatics.IsInstalledPlugin)
        {
            PluginRuntimeUtilities.StageEngineModules(GeneratorStatics.PluginDirectory, GeneratorStatics.TargetType,
                ModuleUtilities.PackageToModuleInfo.Values.Where(module => module.IsPartOfEngine).Select(module => module.ModuleName));
            PluginRuntimeUtilities.StageIdentity(GeneratorStatics.PluginDirectory, GeneratorStatics.TargetType);
        }
        else if (FileExporter.HasModifiedEngineGlue)
        {
            throw new BuildException("Installed UnrealSharp engine bindings differ from the packaged bindings. Install a matching plugin package, or use an UnrealSharp source checkout to rebuild them.");
        }
        if (GeneratorStatics.TargetType != TargetRules.TargetType.Editor)
        {
            return;
        }

        if (GeneratorStatics.IsInstalledPlugin)
        {
            PluginRuntimeUtilities.EnsureEditorRuntime(GeneratorStatics.PluginDirectory);
            return;
        }
        if (!FileExporter.HasModifiedEngineGlue)
        {
            return;
        }
        
        ConsoleUtilities.Log("Engine glue has been modified since the last build. Rebuilding UnrealSharp bindings...");

        List<KeyValuePair<string, string>> actionArgs =
        [
            new("Folders", Path.Combine(GeneratorStatics.ManagedPath, "UnrealSharp")),
            new("TargetConfiguration", GeneratorStatics.TargetConfiguration.ToString())
        ];
        
        UnrealSharpAutomationUtilities.InvokeUnrealSharpAutomation("BuildSolution", actionArgs);
    }
    
    public static void GenerateUserSolution()
    {
        if (GeneratorStatics.TargetType != TargetRules.TargetType.Editor)
        {
            return;
        }
        
        List<KeyValuePair<string, string>> ActionArgs =
        [
            new("TargetType", GeneratorStatics.TargetType.ToString()),
            new("TargetConfiguration", GeneratorStatics.TargetConfiguration.ToString())
        ];
        UnrealSharpAutomationUtilities.InvokeUnrealSharpAutomation("GenerateUserSolution", ActionArgs);
    }
}
