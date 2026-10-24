using AutomationTool;

namespace UnrealSharp.Automation.Staging;

// UE 5.7 and later discover this handler automatically in loaded UAT script modules.
public sealed class UnrealSharpStagingHandler : CustomStagingHandler
{
    protected override bool TryInitialize(ProjectParams Params, DeploymentContext SC)
    {
        return WindowsManagedStaging.IsEnabled(Params, SC);
    }

    public override void GetFilesToStage(ProjectParams Params, DeploymentContext SC)
    {
        WindowsManagedStaging.Stage(Params, SC);
    }
}
