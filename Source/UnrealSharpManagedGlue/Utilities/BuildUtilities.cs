using System.Collections.Generic;
using UnrealBuildTool;
using UnrealSharp.Shared;

namespace UnrealSharpManagedGlue.Utilities;

public static class BuildUtilities
{
    public static void BuildBindings()
    {
        if (GeneratorStatics.TargetType != TargetRules.TargetType.Editor || !FileExporter.HasModifiedEngineGlue)
        {
            return;
        }
        
        ConsoleUtilities.Log("Engine glue has been modified since the last build. Rebuilding UnrealSharp bindings...");

        GlueTarget target = GeneratorStatics.CreateGlueTarget();

        List<KeyValuePair<string, string>> actionArgs =
        [
            new("Folders", target.FrameworkProjectDirectory),
            new("TargetConfiguration", target.Configuration)
        ];
        
        UnrealSharpAutomationUtilities.InvokeUnrealSharpAutomation("BuildSolution", actionArgs);
    }
    
    public static void GenerateUserSolution()
    {
        if (GeneratorStatics.TargetType != TargetRules.TargetType.Editor)
        {
            return;
        }
        
        GlueTarget target = GeneratorStatics.CreateGlueTarget();
        List<KeyValuePair<string, string>> ActionArgs =
        [
            new("TargetType", target.Type),
            new("TargetConfiguration", target.Configuration)
        ];
        UnrealSharpAutomationUtilities.InvokeUnrealSharpAutomation("GenerateUserSolution", ActionArgs);
    }
}
