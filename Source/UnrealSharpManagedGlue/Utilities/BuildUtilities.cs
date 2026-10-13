using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EpicGames.Core;
using UnrealBuildTool;

namespace UnrealSharpManagedGlue.Utilities;

public static class BuildUtilities
{
    public static void StageAutomationModule()
    {
        if (GeneratorStatics.TargetType != TargetRules.TargetType.Editor)
        {
            return;
        }

        // Installed engines do not compile engine automation projects. Keep the
        // already-built module and a relocatable UAT build record inside the plugin.
        string pluginDirectory = GeneratorStatics.PluginDirectory;
        string projectPath = Path.Combine(pluginDirectory, "Build", "Scripts", "UnrealSharp.Automation.csproj");
        string assemblyPath = Path.Combine(pluginDirectory, "Binaries", "Managed", "Automation", "UnrealSharp.Automation.dll");
        string recordDirectory = Path.Combine(pluginDirectory, "Intermediate", "ScriptModules");
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("The precompiled UnrealSharp automation module was not staged by its build.", assemblyPath);
        }
        Directory.CreateDirectory(recordDirectory);

        CsProjBuildRecord record = new()
        {
            Version = CsProjBuildRecord.CurrentVersion,
            ProjectPath = Path.GetRelativePath(recordDirectory, projectPath),
            TargetPath = Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, assemblyPath),
            TargetBuildTime = File.GetLastWriteTime(assemblyPath)
        };
        File.WriteAllText(Path.Combine(recordDirectory, "UnrealSharp.Automation.json"),
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void BuildBindings()
    {
        if (GeneratorStatics.TargetType != TargetRules.TargetType.Editor || !FileExporter.HasModifiedEngineGlue)
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
