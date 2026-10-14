using AutomationTool;
using System;
using System.Collections.Generic;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.BuildCommands;

[Help("Builds the auto-generated UnrealSharp glue projects for the active project and emits the glue load order.")]
[Help("TargetConfiguration=<Config>", "The build configuration (Debug, DebugGame, Development, Shipping, etc.).")]
[Help("TargetType=<Type>", "The target type (Editor, Game, etc.) to build glue for.")]
[Help("OutputDirectory=<OutputDirectory>", "The directory to output the built glue assemblies to.")]
[Help("AddReferences=<true|false>", "Whether to add references to the user project.")]
[Help("ExtraArguments=<Arg>+<Arg>", "Additional arguments forwarded to dotnet build/publish.")]
public class BuildUserGlue : BuildCommand
{
    public override void ExecuteBuild()
    {
        if (this.IsInstalledUnrealSharpBuild())
        {
            throw new AutomationException("BuildUserGlue should not be executed in an installed UnrealSharp build.");
        }

        TargetType TargetType = ParseRequiredEnumParamEnum<TargetType>("TargetType");
        UnrealTargetConfiguration TargetConfiguration = ParseRequiredEnumParamEnum<UnrealTargetConfiguration>("TargetConfiguration");
        string OutputDirectory = ParseRequiredStringParam("OutputDirectory");
        bool AddReferences = ParseParamBool("AddReferences", false);
        string[] ExtraArguments = ParseParamValues("ExtraArguments");

        Build(this, TargetType, TargetConfiguration, OutputDirectory, AddReferences, ExtraArguments);
    }

    public static void Build(BuildCommand command, TargetType targetType, UnrealTargetConfiguration buildConfig, string outputDirectory, bool addReferences, IList<string>? extraArguments = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);

        GlueTarget target = GlueTargetUtilities.Create(command, targetType, buildConfig);
        Build(command, target, outputDirectory, addReferences, extraArguments);
    }

    public static void Build(BuildCommand command, GlueTarget target, string outputDirectory, bool addReferences, IList<string>? extraArguments = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        GlueBuildUtilities.Build(command, target, outputDirectory, addReferences, extraArguments);
    }
}
