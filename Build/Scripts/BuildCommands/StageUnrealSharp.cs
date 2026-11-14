using System.Collections.Generic;
using AutomationTool;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.BuildCommands;

[Help("Packages the UnrealSharp managed code for an installed build")] 
[Help("UEBuildConfig=<Config>", "Optional. The build configuration (Debug, Development, Shipping, Test).")]
[Help("UETargetType=<Type>", "Optional. The target type (Editor, Game, etc.). Defaults to Editor.")]
[Help("TargetPlatform=<Platform>", "Optional. Target platform. Defaults to Win64.")]
[Help("TargetArchitecture=<Arch>", "Optional. Target architecture. Defaults to X64.")]
public class StageUnrealSharp : BuildCommand
{
    public override void ExecuteBuild()
    {
        GlueTarget target = GlueTargetUtilities.Resolve(this);
        string architecture = ParseParamValue("TargetArchitecture", target.Architecture);
        if (architecture.Contains('+'))
            throw new AutomationException("Select one -TargetArchitecture when staging a universal native target.");
        List<KeyValuePair<string, string>> ActionArgs =
        [
            new("ArchiveDirectory", this.GetProjectRootFolder()),
            new("UEBuildConfig", target.Configuration),
            new("UETargetType", target.Type),
            new("TargetPlatform", target.Platform),
            new("TargetArchitecture", architecture),
            new("UnrealSharpTargetManifest", target.ManifestPath)
        ];
        
        CommandUtilities.RunCommand(nameof(PackageProject), this, ActionArgs);
    }
}
