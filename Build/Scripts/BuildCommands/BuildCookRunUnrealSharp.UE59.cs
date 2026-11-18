using AutomationTool;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharp.Automation.BuildCommands;

[Help("Runs BuildCookRun with UnrealSharp staging and the UE 5.9 Wasm prelink boundary.")]
[Help("UnrealSharpRuntimeProduct=<Path>", "For Wasm, select the CoreCLR interpreter or NativeAOT by passing the corresponding portable RuntimeProduct.json at package time.")]
[Help(typeof(ProjectParams))]
public class BuildCookRunUnrealSharp : global::BuildCookRun
{
    public override void ExecuteBuild()
    {
        if (ParseParam("foreign") || ParseParam("foreigncode"))
        {
            throw new AutomationException("BuildCookRunUnrealSharp requires an existing project. Create foreign sample projects with BuildCookRun first.");
        }

        // SetupParams can discover/compile target rules. Validate the explicit
        // runtime/tool inputs before entering that path for a Wasm request.
        WasmPrelink.RejectUnavailableExplicitRequest(this);
        ProjectParams parameters = SetupParams();
        using var wasmBuild = WasmPrelink.ContainsWasm(parameters) ? WasmPrelink.Prepare(this, parameters) : null;

        // Modern engines already use UnrealSharpStagingHandler. Do not also
        // register the pre-5.7 staging callback.
        DoBuildCookRun(parameters);
        if (WasmPrelink.ContainsWasm(parameters))
        {
            WasmStagingProduct.RequireCompleted();
        }
    }
}
