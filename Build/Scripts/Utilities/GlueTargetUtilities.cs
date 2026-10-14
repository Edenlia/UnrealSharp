using AutomationTool;
using UnrealBuildTool;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.Utilities;

public static class GlueTargetUtilities
{
    public static GlueTarget Create(BuildCommand command, TargetType type, UnrealTargetConfiguration configuration,
        string? platform = null, string? architecture = null)
    {
        return new GlueTarget(type.ToString(), configuration.ToString(), command.GetProjectRootFolder(),
            command.ParseParamValue("PluginDirectory"))
        {
            Name = command.ParseParamValue("TargetName"),
            Platform = platform ?? command.ParseParamValue("TargetPlatform"),
            Architecture = architecture ?? command.ParseParamValue("TargetArchitecture"),
            ProjectFile = command.ParseParamValue("Project"),
            BuildAllModules = command.ParseParamBool("BuildAllModules", false)
        };
    }
}
