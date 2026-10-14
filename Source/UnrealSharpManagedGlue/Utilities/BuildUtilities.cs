using System.Collections.Generic;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharpManagedGlue.Utilities;

public static class BuildUtilities
{
    public static void BuildBindings()
    {
        // Preserve the original project workflow: non-Editor UHT generates
        // source; PackageProject compiles it into the archive, not over Editor DLLs.
        if (!GeneratorStatics.Target.IsDistributionBuild && GeneratorStatics.TargetType != TargetType.Editor) return;
        // MSBuild checks deleted outputs and membership-only changes too.
        // BuildPlugin also publishes its non-Editor distribution bundles.
        GlueTargetUtilities.BuildFramework(GeneratorStatics.Target);
        GlueTargetUtilities.BuildGlue(GeneratorStatics.Target);
        GlueTargetUtilities.PublishRuntimeContext(GeneratorStatics.Target);
    }

    public static void GenerateUserSolution()
    {
        if (GeneratorStatics.TargetType != TargetType.Editor || GeneratorStatics.Target.Distribution || string.IsNullOrEmpty(GeneratorStatics.Target.ProjectFile)) return;
        List<KeyValuePair<string, string>> arguments = new()
        {
            new("TargetType", GeneratorStatics.TargetType.ToString()), new("TargetConfiguration", GeneratorStatics.TargetConfiguration.ToString()),
            new("TargetPlatform", GeneratorStatics.TargetPlatform.ToString()), new("UnrealSharpTargetManifest", GeneratorStatics.Target.ManifestPath)
        };
        UnrealSharpAutomationUtilities.InvokeUnrealSharpAutomation("GenerateUserSolution", arguments);
    }
}
