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

Original source snapshots/hash manifests are in original/A and original/B (857 / 852 files). Unreal binary assets were not inspected or edited. A is cb8577e3 with common FString, interface-dispatch and delegate-parameter prerequisites; B is the current fork. Additional prerequisites were introduced only after the matching failures documented below. Deployment records HEAD, patch hash and every copied file hash.

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

## Target-aware publish metadata

Before the environment correction, upstream-aot-package-b produced a 114 MiB Game dylib but omitted load-order manifests/installed flag; its final root defaulted to Editor properties. The fix references/roots emitted manifest assemblies (including otherwise unreferenced glue), carries target/configuration to final publish, copies manifests/flag and expands warnings.

Installed verification: installed-package-b used ordinary production UAT with PackageProject -NativeAOT -UETargetType=Game -UEBuildConfig=Development -TargetPlatform=Mac -TargetArchitecture=arm64. It exited 0 in 120.52 seconds. archives/installed-manifest-b/Binaries/Managed/net10.0 contains the 114 MiB SharpTestProject1.AOT.dylib, GlueCode.LoadOrder.json (PackageTargetFixture), UserCode.LoadOrder.json (PackagingManagedProbe, SharpTestProject1.RuntimeGlue) and UnrealSharpBuild.flag. The aggregate project explicitly references and roots all three manifest assemblies; no Editor-only module appears in the manifests.

The existing installed-dispatch-b/caller was then run with `dotnet run --project <evidence>/runs/installed-dispatch-b/caller/Caller.csproj -c Release -- <evidence>/archives/installed-manifest-b/Binaries/Managed/net10.0/SharpTestProject1.AOT.dylib package`. The actual package's production export passed all 17 applicable bootstrap checks and exited 0. No test-only export is required in package mode. This is export validation, not yet UE game startup.

All 13 publish warnings are preserved: seven core reflection/type/constructor sites, two delegate wrapper discovery/construction sites, two legacy exported-function reflection sites, and two dynamic-load methods retained by the whole-assembly root. They are not suppressed; exercise the generated game path next and remove incompatible code from the AOT path in a separate step.

## Native host and first standalone Game

Non-Editor targets look for `<Project>.AOT.<platform module extension>` in the managed output directory before resolving hostfxr. The AOT export shares the existing bootstrap ABI. A present library with a load/export error is fatal, rather than falling through to JIT. NativeAOT's handle remains loaded until process exit; embedded logical assemblies no longer require separate DLLs and are noncollectible. Editor retains JIT. The original JIT host loading implementation is moved unchanged into ResolveJitEntryPoint.

The shared parameter fixture now runs in a Runtime module, exposed through the original Editor commandlet and a Game-only `-ParameterBufferTests` startup hook. The managed fixture no longer references the unused Editor-only probe assembly. Native builds use the same canonical entries; archive performs cook/stage/package only with skipbuild/skipbuildeditor/nocompileeditor.

- loader-editor-b / loader-game-b: Installed canonical builds passed (144.24 / 73.33 seconds). loader-jit-b retained all 22 Editor checks.
- loader-archive-b stopped after a successful cook: a pre-task generated project Automation binary still contained a staging adapter whose production type is absent from this branch. Its existing csproj already disables the adapter when its source is absent. A normal `dotnet build <B>/Build/UnrealSharp/UnrealSharpProject.Automation.csproj -c Development -p:EngineDir=<Installed>/Engine` rebuilt the expected empty module. No production change, Engine modification, native cache cleanup, or AutoBuild edit was made.
- loader-archive-b2 succeeded in 89.49 seconds; standard Mac staging/package Xcode tasks performed copying/signing, with no C++ compile/link action. loader-publish-b then published AOT directly into the app's Contents/UE/SharpTestProject1 payload, exit 0 (129.27 seconds).
- loader-runtime-b reached the tests but could not write the external report from the app sandbox. Mac's graceful RequestExitWithStatus ignored status 3, so process exit 0 was not acceptance. loader-runtime-b2 caught a second test-infrastructure error: extra literal quotes duplicated macOS argument quoting. Both failed attempts are preserved.
- loader-runtime-b3 passed **22/22 checks**, exactly matching baseline A's Installed JIT check names, and exited normally (1.33 seconds). The report is written inside the app's normal container Saved directory and copied into evidence. The runner now requires a passing report independently of process exit status. DYLD_PRINT_LIBRARIES recorded SharpTestProject1.AOT.dylib and **zero hostfxr/CoreCLR/hostpolicy images**. The package contains logical load-order manifests and no managed runtime DLLs. comparison.json records the check/image comparison.

This proves the real generated Game path for current scalar/string/struct/object/ref/out cases. Broader generated features, explicit unload rejection, negative loader cases and JIT hot reload remain separate gates.

## Generated inherited functions and live Editor reload

Added five real generated-function cases: bool/FName/ref/out/return, false bool, inherited virtual override, inherited no-argument return and repeat invocation after managed GC. The Python Editor controller calls the reflected runner and reads its report through a UFunction. These are UE calls, not only direct C# method tests.

- live-functions-b2: normal Editor detected the changed fixture, unloaded/recompiled/reloaded PackagingManagedProbe in 2.85 seconds and passed 27 checks.
- live-functions-a initially failed at startup after a Game build because Editor-only PackageTargetProbe.dll was absent. Ordinary live-editor-glue-a BuildUserGlue restored the Editor output; no production workaround or native rebuild was needed. live-functions-a2 then completed Hot Reload in 2.94 seconds and passed the same 27 checks. The task-owned crash reporter and accidental empty Editor instance were closed; no user process was terminated.
- functions-publish-b: normal production Game NativeAOT publish passed in 64.00 seconds. functions-runtime-b executed the existing standalone app with that payload, passed the same 27 check names and exited normally in 1.96 seconds. Loaded-image evidence again contains the AOT dylib and no hostfxr/CoreCLR/hostpolicy.
- Existing unrelated DefaultABDeepOptions::Position initialization warnings remain in full Editor startup logs; this step does not claim a warning-free project.

No production dispatch change was required. Static UFunction through UE reflection, broader features and AOT unload rejection remain subsequent steps.

## Static UFunction through native reflection

The next fixture calls a generated static UFunction using its real native function pointer, reflected parameter offsets and the class default object through InvokeNativeStaticFunction. It does not directly invoke the C# static body. live-static-a and live-static-b both passed 28 checks after normal Editor Hot Reload; static-publish-b succeeded in 65.23 seconds, and static-runtime-b passed the identical 28 checks in the standalone AOT Game. No production dispatcher/generator change was needed.

## Generated properties and custom structs

Added eight cases for constructor/native-pointer ordering through property writes, bool/int/Unicode string/FName properties, clearing retained strings, UObject identity/null, native UStruct default construction, custom struct properties containing FString/FVector and generated struct input/ref/out/return marshaling. live-properties-a and live-properties-b both passed 36 checks after actual Hot Reload. properties-publish-b passed in 70.66 seconds; properties-runtime-b passed the identical 36 checks in the standalone NativeAOT Game (1.88 seconds).

The new live.mjs driver was exercised in both successful rounds. It snapshots the exact changed fixture sources/hashes, waits for normal Editor readiness and reload success, checks the exact result count and closes its own Editor. No production property/struct changes were required.

## Generated containers

live-containers-a and live-containers-b both exposed the same pre-existing API contract: ContainerProperty.ExportSetter deliberately generates NotSupportedException. Whole-container assignment is unsupported on baseline and candidate. The corrected fixture explicitly verifies rejection without mutation, then uses the supported in-place APIs. This is not an AOT production fix.

live-containers-a2 / live-containers-b2 passed 44 checks after Hot Reload: array insert/index/remove/enumeration/clear/repopulate, FName set uniqueness/removal, Unicode string map update/lookup/removal/enumeration, and custom-struct arrays owning FString values. containers-publish-b passed in 65.47 seconds; containers-runtime-b passed the identical 44 checks in the standalone AOT Game. Failed initial rounds remain in evidence. The driver now waits for its Editor to finish closing even when an assertion fails.

## Interface dispatch prerequisite

live-interfaces-b and live-interfaces-a reproduced the same native assertion in UCSFunctionBase::InvokeManagedMethod. The generated wrapper used the interface declaration's UFunction for non-event calls. Interface declarations intentionally have no managed method handle; execution must resolve the implementation on the object's class. Both task-owned Editors hung after the fatal error and were stopped after SIGTERM did not end them. Their complete logs and failed results remain in evidence; no unrelated process was terminated.

UnrealFunction.ExportWrapperMethod now resolves all interface methods on the instance, as it already did for events. Static interface metadata still supplies the parameter layout. The same two-condition fix is applied to baseline A as a common prerequisite, with no native source/build change.

- interfaces-generator-a / interfaces-generator-b: normal production BuildSolution passed, 44.61 / 38.37 seconds.
- live-interfaces-fixed-a / live-interfaces-fixed-b: 49 matching checks after normal Editor Hot Reload. New cases cover wrapper/UObject identity, callable methods with and without parameters, event implementation, and interface parameter/return invocation after managed GC.
- interfaces-publish-b: production NativeAOT publish passed in 88.51 seconds. interfaces-runtime-b passed the identical 49 checks in the standalone Game.

This fixes an existing JIT defect exposed by the AOT coverage expansion; it is not an Engine/Project plugin-selection workaround.

## Delegate parameter marshaling and native initialization

live-delegates-a / live-delegates-b reproduced a native FScriptDelegate access-detector ensure in both slots. Checkpoints showed property assignment succeeded and the fault occurred while marshaling the function parameter. DelegateProperty incorrectly ignored its supplied buffer, addressed NativeObject plus the parameter offset, and cached parameter values as though they were properties. Roslyn callers also passed uninitialized stack storage, unlike the existing UHT wrapper path.

Delegate fields retain their property cache; delegate parameters now decode each invocation from the actual parameter buffer, and all delegate writes honor that buffer. Roslyn calls initialize native parameter values through the existing InitializeFunctionParams binding before marshaling. No C++ change was needed. These matching prerequisites were applied to baseline A after the reproductions. Parameter destruction is a separate follow-up.

- delegates-generator-b / delegates-generator-a: normal managed BuildSolution succeeded (38.39 / 7.57 seconds).
- live-delegates-fixed-a / live-delegates-fixed-b: **56 matching checks** after Hot Reload, including single binding, actual delegate-parameter invocation, repeated calls, unbound delegate without stale caching, two multicast targets after GC, removal and clear.
- delegates-publish-b: production AOT publish succeeded in 89.88 seconds; delegates-runtime-b passed the same 56 checks in the standalone Game. All three positive logs were explicitly checked for native ensure/assertion/critical-error markers and contained none. The live driver now rejects those markers even if a managed report passes.

The failed task-owned Editors were stopped after they remained blocked on the ensure despite SIGTERM; raw failure logs remain preserved. No unrelated processes were terminated.

## Module startup and NativeAOT unload boundary

NativeAOT cannot unload its shared library or embedded assemblies. Previously Plugin.Unload could shut down modules and free every handle in the shared default context before dereferencing its null load context. The unmanaged unload callback now warns and returns before mutation; direct Plugin.Unload throws PlatformNotSupportedException before shutdown. PluginLoadContext and the dynamic unload implementation are excluded from AOT compilation. JIT loading/unloading code is unchanged.

The generated UModule fixture checks assembly/module identity, exactly one startup after native bindings are ready, no repeated startup on Load, and survival through managed GC. AOT additionally attempts both unload paths and then constructs/calls a generated UObject after GC to verify handles remain intact.

- lifecycle-runtime-build-b and lifecycle-editor-glue-b succeeded through normal managed UAT. The first fixture had an incorrect UModule using directive (live-lifecycle-b / b2); the failed build/startup evidence is retained. Corrected the fixture before retrying.
- live-lifecycle-b3 / live-lifecycle-a passed **60 identical JIT checks** after Hot Reload. B's console explicitly records startup 1, shutdown 1 during reload, then the new module's startup 1.
- lifecycle-publish-b passed in 86.92 seconds. Dynamic-load trimming warnings disappeared because the code is absent from the AOT assembly; the remaining **11 reflection warnings are unsuppressed**.
- lifecycle-runtime-b passed **63 checks** in 2.02 seconds: the same 60 common checks plus three AOT unload rejection/continued-use checks. Loaded images contain no hostfxr/CoreCLR/hostpolicy, and the run has no native ensure/assertion.

## Generated parameter lifetime

Roslyn-generated callers now pair native parameter initialization with destruction in a finally block, covering successful calls, output conversion and input marshaling exceptions. The new native binding visits exactly NumParms, matching initialization; Blueprint-local fields beyond ParmsSize are excluded. This common prerequisite is deployed identically to A and B.

- params-lifetime-editor-a / b and params-lifetime-game-a / b all passed through their unchanged Installed AutoBuild entries. Complete Editor runner time was 10.02 / 10.92 seconds; Game runner time was 55.51 / 56.81 seconds.
- params-lifetime-generator-a / b and params-lifetime-editor-glue-a / b passed through ordinary managed UAT.
- live-params-lifetime-a / b passed **63 matching checks** after Hot Reload. New cases exercise array input/ref array/out map/array return, previously returned copies after 32 subsequent calls, and a deliberate marshaling exception after allocating an 8 KiB FString followed by a successful call.
- params-lifetime-archive-b completed in 76.12 seconds; params-lifetime-publish-b passed in 114.21 seconds. params-lifetime-runtime-b passed **66 checks** (63 common plus 3 AOT-only) in the new standalone app. Its image log contains no hostfxr/CoreCLR/hostpolicy and no native ensure/assertion. comparison.json records the common-case comparison.

These are functional ownership/lifetime checks, not a quantified memory-leak benchmark.

## Unmanaged exception boundary

The exception fixture retrieves the actual managed callback table from native UE, resolves generated Invoke wrappers through GetManagedMethod, and calls InvokeManagedMethod through its unmanaged pointer. A deliberately throwing UFunction must return failure with its Unicode exception text; a subsequent generated return-valued callback must succeed. Fixture-owned handles and native error FString are released in finally. UE's crash-on-Blueprint-exception policy remains unchanged.

live-exception-a / b passed **65 identical checks** after normal Hot Reload. exception-publish-b passed in 66.08 seconds; exception-runtime-b passed **68 checks** in 2.14 seconds (65 common plus 3 AOT-only). No dispatcher change was required. The expected `Exception during InvokeManagedMethod: Expected callback failure: 中文 é` error is present in all three logs; no native ensure/assertion occurred and the AOT run loaded no JIT runtime images.

## Negative native loader boundaries

negative.mjs preserves the task-owned archived AOT dylib, tests an invalid library plus two tiny standalone .NET NativeAOT libraries, and restores the production bytes in finally. It never compiles Unreal C++ or changes the Engine, project build configuration or package native executable.

loader-negative-b3 passed all three expected failures: failed dlopen, missing InitializeAotRuntime, and initialization returning a Unicode error. Each process terminated through UE's fatal-error path (SIGSEGV on this Mac), produced the expected diagnostic, produced no passing fixture report, and loaded no hostfxr/CoreCLR/hostpolicy. This is deliberate failure behavior, not a runtime crash during a positive case.

Earlier loader-negative-b / b2 were inconclusive because the asynchronous log thread had not written the fatal diagnostic before termination. Their logs are retained; the test now uses the existing runtime options NoLogThread and forcelogflush to capture evidence. No production workaround was added. restored.json verifies identical production SHA-256 before/after. loader-negative-restored-b then passed all **68** positive checks again.

## Installed UE verification

- installed-baseline-a / installed-candidate-b deployed sources without the plugin-selection workaround.
- installed-editor-a / installed-editor-b: canonical Installed Editor builds succeeded, 129.28 / 129.61 seconds. Both used the existing AutoBuild_Mac.sh without option changes.
- installed-glue-a / installed-managed-a / installed-glue-b / installed-managed-b: ordinary production UAT completed successfully with no renamed/source-linked dispatcher.
- installed-runtime-a / installed-runtime-b: both Installed UnrealEditor-Cmd runs exited 0 with **22 PASS**, 25.93 / 25.77 seconds. This revalidates the FString prerequisite and B's shared JIT bootstrap against A.
- installed-dispatch-b: ordinary UnrealSharp.Automation.dll generated the export through a test-only ProjectReference command; real NativeAOT publish succeeded in 35.74 seconds and the unmanaged caller passed **31 checks**. Seven known reflection trimming warnings remain visible. No production sources were renamed or linked into a substitute Automation assembly.
- installed-game-b initially stopped in the existing AutoBuild wrapper before UBT: its manifest referenced the original GlueGenerator.Tests/OutParameters.cs, removed by our deployment. Restored that pre-task fixture byte-for-byte in both slots. Deployment now removes only previously deployed inputs, and records retained local inputs. installed-retained-b verified the restored file remains untouched. No AutoBuild script/configuration was changed. installed-game-b-restored then passed the canonical Installed Game build in 73.89 seconds.
