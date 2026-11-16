// Host lifecycle only. Link alongside the real production Browser + Utils
// libraries, never libBrowserHost or a second owned host/asset loader.
addToLibrary({
    $UNREALSHARP_WASM_LIFECYCLE: {
        phase: 0,
        result: 0,
        read: function (address) {
            if (!Number.isSafeInteger(address) || address < 0 || address % 4 || address + 4 > HEAP32.byteLength) {
                throw new Error("Invalid UnrealSharp lifecycle address");
            }
            if (!(HEAP32.buffer instanceof SharedArrayBuffer)) {
                throw new Error("UE Wasm static host requires its normal pthread shared memory");
            }
            // Division preserves offsets above 2 GiB; bitwise shifts would truncate/sign them.
            return Atomics.load(HEAP32, address / 4);
        },
        install: function () {
            if (typeof ENVIRONMENT_IS_PTHREAD !== "undefined" && ENVIRONMENT_IS_PTHREAD) return;
            var previous = Module["onExit"];
            Module["onExit"] = function (code) {
                var effective = code;
                try {
                    if (!UNREALSHARP_WASM_LIFECYCLE.phase) throw new Error("CLR owner lifecycle was never registered");
                    var phase = UNREALSHARP_WASM_LIFECYCLE.read(UNREALSHARP_WASM_LIFECYCLE.phase);
                    var result = UNREALSHARP_WASM_LIFECYCLE.read(UNREALSHARP_WASM_LIFECYCLE.result);
                    if (phase !== 4 && phase !== 5) throw new Error("CLR owner did not complete shutdown, phase=" + phase);
                    effective = result || code;
                    if (effective) err("UE_RUN ERROR UnrealSharp owner shutdown result " + effective);
                    if (phase === 4 && result === 0 && code === 0) {
                        out("UNREALSHARP_WASM_OWNER_CLOSED " + JSON.stringify({ phase: phase, result: result, exitCode: code }));
                    }
                } catch (error) {
                    effective = effective || 71;
                    err("UE_RUN ERROR UnrealSharp lifecycle observer: " + error);
                }
                // WasmStartup already set ABORT and terminated workers before onExit.
                // Read shared atomics only: no new Wasm/CLR entry on this main JS thread.
                if (previous) previous(effective);
            };
        }
    },
    $UNREALSHARP_WASM_LIFECYCLE__postset: "UNREALSHARP_WASM_LIFECYCLE.install()",

    UnrealSharpWasm_RegisterLifecycle__sig: "vpp",
    UnrealSharpWasm_RegisterLifecycle__proxy: "sync",
    UnrealSharpWasm_RegisterLifecycle__deps: ["$UNREALSHARP_WASM_LIFECYCLE"],
    UnrealSharpWasm_RegisterLifecycle: function (phase, result) {
        UNREALSHARP_WASM_LIFECYCLE.phase = Number(phase);
        UNREALSHARP_WASM_LIFECYCLE.result = Number(result);
    },

    UnrealSharpWasm_BeginShutdown__sig: "v",
    UnrealSharpWasm_BeginShutdown__deps: ["$DOTNET", "$BROWSER_UTILS", "BrowserHost_ShutdownDotnet"],
    UnrealSharpWasm_BeginShutdown: function () {
        DOTNET.isAborting = true;
        dotnetBrowserUtilsExports.abortBackgroundTimers();
    }
});
