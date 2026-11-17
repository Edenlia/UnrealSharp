using AutomationTool;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharp.Automation.Staging;

public partial class UnrealSharpStagingHandler
{
    public override void PostStagingFileCopy(ProjectParams Params, DeploymentContext SC)
    {
        if (WasmPrelink.IsWasm(SC.StageTargetPlatform.PlatformType))
            WasmStagingProduct.ObserveStageCopy(Params, SC);
    }
}
