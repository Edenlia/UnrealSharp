using System.Collections.Generic;
using AutomationTool;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.BuildCommands;

[Help("Builds the exact glue project set recorded by the selected native UHT target.")]
public class BuildUserGlue : BuildCommand
{
    public override void ExecuteBuild()
    {
        GlueTarget target = GlueTargetUtilities.Resolve(this);
        GlueBuildUtilities.BuildGlue(target, ParseParamValue("OutputDirectory", target.OutputDirectory), ParseParamValues("ExtraArguments"));
    }

    public static void Build(BuildCommand command, TargetType targetType, UnrealTargetConfiguration buildConfig,
        string outputDirectory, bool addReferences, IList<string>? extraArguments = null)
    {
        GlueTarget target = GlueTargetUtilities.Resolve(command);
        if (target.Type != targetType.ToString() || target.Configuration != buildConfig.ToString())
            throw new AutomationException("Selected UnrealSharp glue context does not match the requested target type/configuration.");
        GlueBuildUtilities.BuildGlue(target, outputDirectory, extraArguments);
        // References are supplied by UnrealSharp.References.targets for the selected context.
    }
}
