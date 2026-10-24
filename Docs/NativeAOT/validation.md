# NativeAOT change and verification journal

## Current verification authority

The user clarified on 2026-10-24 that both SharpTestProject slots must use **Installed UE 5.8**, not the source Engine checkout. The existing Editor/Game AutoBuild entries point to /Users/Shared/Epic Games/UE_5.8 (5.8.3, CL 58210709); their commands/options remain unchanged. Installed UE has no UnrealSharp plugin. Project-local plugins are the test subjects.

Earlier source-Engine results are historical, not Installed UE acceptance. The detailed journal is preserved at <WORKSPACE>/UnrealEngine/Tests/NativeAOT/eden-main/source-engine-history.md, alongside raw evidence in runs/.

## Removed environment workaround

The source Engine's own UnrealSharp copy caused plugin-selection and same-named Automation collisions. At the user's request, dedicated fix 8e1b3a7f was removed from local eden-main history and baseline A. The generic renamed/source-linked UAT dispatcher was removed. Normal commands now use Installed UE's ordinary UAT dispatcher. No push occurred.

Only our subsequent commits were replayed; externally created merge 4899400f was retained. Historical IDs map as follows:

| Historical ID | Current ID | Change |
| --- | --- | --- |
| 0043c08f | ca5e79d5 | A/B fixtures and evidence |
| d3e9377c | a9346f98 | FString length |
| 97767123 | d9132e0c | Shared bootstrap/export ABI |
| 8a2e1d50 | c0cde37e | Actual AOT dispatch/construction probe |

## Upstream merge — 4899400f

Merged upstream/nativeaot b19374cee4cf25a6a98fc3e0fda4ab063b80cfdd. Preserve current UTF-8 initialization/result ABI, hostfxr conversion, invocation ABI and ref/out fixes. Adapt delayed registration to current FieldName/RegisterManagedType; preserve target-aware packaging, project/DLL references, existing BuildingAOT/NATIVE_AOT mapping and explicit AOT opt-in.

The prototype's old loader/export ABI and incomplete static UHT invocation change were deferred. This merge alone is not complete AOT support. It was committed externally during testing; all 816 deployed files matched the reviewed candidate.

Original source snapshots/hash manifests are in original/A and original/B (857 / 852 files). Unreal binary assets were not inspected or edited. A is cb8577e3 with only the common FString prerequisite; B is the current fork. Deployment records HEAD, patch hash and every copied file hash.

## FString length — a9346f98

Both source-Engine controls failed after 18 checks: the cleared UObject was null, but an empty FString still returned old text. FromNative ignored ArrayNum while FString retained storage. It now returns empty for ArrayNum <= 1 and copies exactly ArrayNum - 1 UTF-16 units otherwise.

Historical corrected A/B each passed 22 real UE checks, covering Unicode, 8192-character output, null objects, ref/out and mixed returns. Installed UE revalidation is pending below.

## Bootstrap/export — d9132e0c

RuntimeBootstrap.Initialize shares the result layout and implementation between JIT and AOT. Keep the original hostfxr class/method/ABI. The generated root exports void InitializeAotRuntime with UTF-8 path, callbacks and result pointer. AOT skips Editor MSBuild discovery; JIT retains it.

Historical bootstrap-export-b published actual runtime project references into an osx-arm64 NativeAOT dylib (compiler/runtime pack 10.0.12). The caller passed 18 checks: symbol resolution, 4097-byte result, all 11 callback pointers and sentinels, Unicode directory/native bind callback, duplicate-init failure and bounded error text. JIT retained 22 UE checks. The test never unloads the AOT library.

Seven reflection trimming warnings were preserved, not suppressed: wrapper/function/struct method lookup, assembly type lookup, struct/object construction. Bootstrap success does not prove full game-type retention or packaged startup.

## Existing dispatch — c0cde37e

Historical dispatch-probe-b2 passed 31 checks including bootstrap. Explicitly rooted mechanism fixtures call the actual unmanaged table: type lookup, uninitialized allocation, constructor ordering, inherited/private methods, virtual calls, input/ref/out/return buffers, no-argument calls after GC, static bodies, Wrap, struct initialization, DynamicInvoke and disposal. Production dispatch needed no change. The first fixture attempt failed with CS1686 and was corrected before the passing run.

Keep the current ABI. Test-only roots isolate the mechanism from the remaining application trimming and generated-metadata gates.

## Publish metadata work in progress

Before the environment correction, upstream-aot-package-b produced a 114 MiB Game dylib but omitted load-order manifests/installed flag; its final root defaulted to Editor properties. The uncommitted fix references/roots emitted manifest assemblies (including otherwise unreferenced glue), carries target/configuration to final publish, copies manifests/flag and expands warnings. Old-environment manifest-aot-package-b published successfully. This change still needs Installed UE verification and is not committed.

## Installed UE verification

- installed-baseline-a / installed-candidate-b deployed sources without the plugin-selection workaround.
- installed-editor-a / installed-editor-b: canonical Installed Editor builds succeeded, 129.28 / 129.61 seconds. Both used the existing AutoBuild_Mac.sh without option changes.
- installed-glue-a / installed-managed-a / installed-glue-b / installed-managed-b: ordinary production UAT completed successfully with no renamed/source-linked dispatcher.
- installed-runtime-a / installed-runtime-b: both Installed UnrealEditor-Cmd runs exited 0 with **22 PASS**, 25.93 / 25.77 seconds. This revalidates the FString prerequisite and B's shared JIT bootstrap against A.
- installed-dispatch-b: ordinary UnrealSharp.Automation.dll generated the export through a test-only ProjectReference command; real NativeAOT publish succeeded in 35.74 seconds and the unmanaged caller passed **31 checks**. Seven known reflection trimming warnings remain visible. No production sources were renamed or linked into a substitute Automation assembly.
