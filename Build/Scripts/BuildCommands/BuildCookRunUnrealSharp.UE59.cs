using AutomationTool;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharp.Automation.BuildCommands;

[Help("Runs BuildCookRun with UnrealSharp staging and the UE 5.9 Wasm prelink boundary.")]
[Help(typeof(ProjectParams))]
public class BuildCookRunUnrealSharp : global::BuildCookRun
{
    public override void ExecuteBuild()
    {
        if (ParseParam("foreign") || ParseParam("foreigncode"))
        {
            throw new AutomationException("BuildCookRunUnrealSharp requires an existing project. Create foreign sample projects with BuildCookRun first.");
        }

        // SetupParams can discover/compile target rules. Reject an explicit Wasm
        // request before entering that path while its native product is absent.
        WasmPrelink.RejectUnavailableExplicitRequest(this);
        ProjectParams parameters = SetupParams();
        if (WasmPrelink.ContainsWasm(parameters))
        {
            WasmPrelink.Prepare(this, parameters);
        }

        // Modern engines already use UnrealSharpStagingHandler. Do not also
        // register the pre-5.7 staging callback.
        DoBuildCookRun(parameters);
    }
}
