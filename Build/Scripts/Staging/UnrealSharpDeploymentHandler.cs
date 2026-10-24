using AutomationTool;
using UnrealBuildTool;

namespace UnrealSharp.Automation.Staging;

// UE 5.6 has no automatic staging hook. Opt in with -customdeployment=UnrealSharp
// or WindowsTargetSettings.CustomDeployment; never replace an existing handler.
[CustomDeploymentHandler("UnrealSharp")]
public sealed class UnrealSharpDeploymentHandler(Platform platform) : CustomDeploymentHandler(platform)
{
    public override bool SupportsPlatform(UnrealTargetPlatform Platform)
    {
        return Platform == UnrealTargetPlatform.Win64;
    }

    public override void PostGetFilesToDeployOrStage(ProjectParams Params, DeploymentContext SC)
    {
        WindowsManagedStaging.Stage(Params, SC);
    }
}
