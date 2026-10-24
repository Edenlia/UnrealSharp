# NativeAOT validation — 2026-10-24

Windows x64 Development Game acceptance passed on UE 5.8.3 Installed Engine (CL 58210709), with .NET SDK 10.0.400 and NativeAOT 10.0.11. This establishes the tested scope, not other platforms, configurations, arbitrary third-party dependencies or dynamic-code compatibility.

## Source and environment

- Base: `cb8577e3a47b5bdc5454239a17f61a6282a50ae2` (fork main and upstream main).
- Upstream NativeAOT: `b19374cee4cf25a6a98fc3e0fda4ab063b80cfdd`; integrated by merge `3a3077cd`.
- Interface implementation dispatch fix: `c25edd75`.
- Runtime/package/fixture snapshot: `d95435db`. Later validation documentation and runner changes do not change runtime sources.
- A: `SharpTestProject`, first base JIT, then candidate JIT.
- B: `SharpTestProject1`, first candidate JIT, then candidate NativeAOT.
- Both existing AutoBuild configurations select `C:/Program Files/Epic Games/UE_5.8`, Win64 Development. Native compilation used only their existing `AutoBuild.bat` and `AutoBuildGame.bat`, unchanged.
- Original plugins and project text are recoverable under each project's `Saved/UnrealSharpAB/20261024-nativeaot/before`. A's built base and initial generated outputs are also preserved separately.

Final source manifests cover 802 build-related files per deployed plugin, all matching the candidate after CRLF/LF normalization. Actual file SHA256 values are retained. Both packaged Games ran from paths containing Chinese characters.

## Functional acceptance

| Check | Result |
| --- | --- |
| Base JIT versus initial candidate JIT | Constructor 11, scalar 42, property 123, ref 10/out 20, Unicode roundtrip and module startup pass on both. |
| Standalone actual NativeAOT export library | Generated NativeBinds scalar/ref-out/struct/missing-binding/duplicate-initialization mask 31; four lookups, three calls. Rooted constructor and current instance method-pointer ABI probe returns 1. |
| Expanded Editor JIT | Ten groups pass, mask 1023; static/inherited calls 42 and native ReceiveTick override 125. |
| Real Editor C# Hot Reload | 101 → 202 → 101, module shutdown/startup twice, exact original source bytes restored. |
| Candidate JIT and AOT packaged Games | Same ten groups: primitive/enum, string/name/text, structs, arrays/maps/sets, object/weak/soft references, single/multicast delegates, interface, inheritance, managed GC. |
| Native lifetime | UObject becomes invalid after native GC; managed Dispose counter increases from 0 to 1. |
| Published dependencies/content | UTF-8 text read from AppContext.BaseDirectory and native dependency export returns 42. |
| Exception boundary | Expected managed exception logged; control returns to native and the following call succeeds, exit 0. Tested independently in both modes. |
| Actual loaded modules | JIT loads packaged hostfxr/coreclr. AOT loads `<Project>.AOT.dll` and the native fixture, with no hostfxr/coreclr observed. Dynamic-code flag is false in AOT. |
| Missing DLL / missing entry export | Both fail with precise diagnostics and exit 3; original native library restored and hash-verified. |
| Unsupported package options | Editor/Win64 and Game/Linux NativeAOT rejected before publishing. |
| Repeated launch and process shutdown | All twelve alternating samples pass, one module startup each and normal engine exit, exit 0. |

The existing Game shutdown path does not call managed `IModuleInterface.ShutdownModule`; this behavior is unchanged and is separate from clean process exit. JIT unload/Hot Reload does call it. NativeAOT unload and Hot Reload are explicitly unsupported.

The interface fixture exposed a current-main bug: wrappers resolved the interface declaration's UFunction, which has no implementation thunk. The fix resolves and caches the implementing object's function. Parameterless void/return and parameterized interface calls pass in both runtime modes.

## Generated output

- Initial identical-fixture comparison: 21,991 files on each side; two intentional registration changes plus 174 whitespace-only differences from the upstream prototype. The upstream generated whitespace change was removed.
- Final base-to-candidate Editor comparison: 21,991 → 21,999 files; all production UHT output unchanged. Eight added files belong to the expanded fixture. The two changed fixture files add deferred type/module registration and the Hot Reload probe method. Full diff: 824 records.
- Final candidate JIT/AOT Game comparison: 15,239 files on each side, **zero byte differences** across plugin UHT, project UHT and the complete fixture's emitted generator output.
- Complete manifests, diffs and per-file classifications are retained; these are not sampled excerpts.

## Measurements

Three sequential A–B–B–A blocks, six independent processes per mode. Each sample first runs the functional matrix, warms 1,000 calls, then measures 50,000 scalar calls and 50,000 native-property roundtrips. Checksums are 2,100,000 and 6,150,000 in every sample. No UE builds or Editors ran during these measurements.

| Metric | JIT median [min–max] | NativeAOT median [min–max] |
| --- | --- | --- |
| 50,000 scalar calls | 10.126 ms [10.011–10.533] | 10.625 ms [10.514–10.720] |
| 50,000 property roundtrips | 10.365 ms [10.336–10.442] | 10.572 ms [10.472–10.757] |
| Full process wall time | 2,190 ms [2,153–3,592] | 2,101 ms [2,096–3,603] |

This workload shows no managed/native call-speed advantage for AOT. The call batches include native ProcessEvent and parameter/property access overhead. Full process time includes the matrix and shutdown, and its first samples are substantially slower; it is not an isolated or cold-start measurement. Export lookup/registration time was not separately instrumented. Do not extrapolate these numbers to gameplay performance.

| Managed deployment directory | JIT | NativeAOT |
| --- | ---: | ---: |
| Bytes including PDBs | 138,517,045 | 472,525,109 |
| Bytes excluding PDBs | 129,890,413 | 134,970,981 |
| Files | 232 | 19 |

These totals include the same fixtures and published content; AOT keeps whole-assembly roots and large debugging symbols. The present implementation does not establish a size reduction. Latest observed managed package commands took 31 seconds for JIT and 113 seconds for AOT; these single runs are recorded build costs, not controlled build-performance comparisons.

## Diagnostics and evidence

The final AOT publish reports IL2026, IL2065, IL2067, IL2070, IL2072, IL2075, IL2077 and the pre-existing fixture's IL3000. Runtime/glue/user assembly roots cover the exercised reflection sites; per-site interpretation is in `NativeAOT.md`. No blanket warning suppression was added. `PluginLoadContext` is excluded from AOT. Third-party libraries and code depending on Assembly.Location must still be reviewed.

Machine-local evidence root: `C:/Users/edenlia/Workspace/CodeProjects/UnrealSharp-reports/nativeaot`. Raw project evidence: `C:/Users/edenlia/Workspace/UEProjects/{SharpTestProject,SharpTestProject1}/Saved/UnrealSharpAB/20261024-nativeaot`.

Key files:

- `candidate-source-manifest.json` in each project; `JIT-artifact-manifest.json`, `NativeAOT-artifact-manifest.json`, and `artifact-summary.json` in the report folder.
- `base-candidate-editor-*-manifest.json`, `base-candidate-editor.diff`, `base-candidate-editor-classified.json`, and `candidate-ab-game-*` in the report folder.
- `ab-samples.json`, `ab-summary.json`; per-project `ab-01-JIT` through `ab-12-JIT` JSON/stdout/stderr logs according to the alternating order.
- A: `candidate-a-editor-build.log`, `candidate-a-game-build.log`, `candidate-a-editor-matrix.log`, `candidate-a-cook-validation.log`, `candidate-a-stage-matrix.log`, `candidate-a-package-jit.log`, `candidate-matrix-jit.*`, `candidate-exception-jit.*`.
- B: `candidate-matrix-game-build.log`, `candidate-package-aot-5.log`, `candidate-final-aot.*`, `candidate-matrix-aot.*`, `candidate-exception-aot.*`, `loader-failures.json`, `unsupported-*.log`, `hotreload-result.json`, `candidate-hotreload.stdout.log`, `export-probe/runtime.log`.

Earlier failures remain in the raw logs: a shared UBT log lock from another build (waited and retried unchanged), test-harness API/property errors, initial AOT aggregate reference resolution, and an obsolete map referring to a historical test actor. Acceptance uses the corrected fixtures and a new map created through Editor APIs. No native Live Coding build was requested, no native build flags were altered, and no Unreal asset bytes were inspected or patched.
