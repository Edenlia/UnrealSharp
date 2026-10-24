# NativeAOT implementation plan

## Working agreement

Implement on `eden-main` in small, reviewable steps. Each functional change needs a focused executable check before its own commit. Record what changed, the exact source identity, commands, results and limitations. Never describe a successful build as successful runtime validation. No push or remote publication is authorized.

The user explicitly authorized UnrealSuperAgent and the existing `SharpTestProject` / `SharpTestProject1` projects for A/B testing. On 2026-10-24 the user clarified that both projects must use **Installed UE 5.8**, not the source Engine checkout. Use their existing Installed AutoBuild entries unchanged. Native builds and Editor/game verification are sequential; preserve unrelated processes, project fixtures and workspace changes.

## Baseline and sources (2026-10-23, America/Los_Angeles)

- Fork: `/Users/shuyangzhang/Workspace/CodeProjects/UnrealSharp`, clean `eden-main`, initial commit `cb8577e3` (also current fetched upstream/main).
- NativeAOT branch: upstream/nativeaot `b19374cee4cf25a6a98fc3e0fda4ab063b80cfdd`; feature commit `a6c9f956`, followed by a merge of then-current main. 24 changed paths, 375 insertions / 129 deletions relative to its merge base with eden-main. Upstream describes the first working AOT build as very unstable.
- A: `/Users/shuyangzhang/Workspace/UEProjects/SharpTestProject/SharpTestProject.uproject`.
- B: `/Users/shuyangzhang/Workspace/UEProjects/SharpTestProject1/SharpTestProject1.uproject`.
- Both projects are existing non-Git test directories. Preserve their existing source and plugin contents before deployment. Keep task fixtures reproducible in this repository.
- Native verification: existing `AutoBuild_Mac.sh` / `AutoBuild.json` (Editor) and `AutoBuild_Game_Mac.sh` / `AutoBuild_Game.json`, using `/Users/Shared/Epic Games/UE_5.8` (5.8.3, CL 58210709). Each JSON fixes cwd, project, Target, Mac Development, default architecture, `-WaitMutex` and `-FromMSBuild`. The source-engine runs performed before the clarification are historical only. This engine correction is explicitly requested by the user, not a build-failure workaround.
- Installed UE contains no UnrealSharp plugin; both tests use their project-local copy. Do not modify the integrated source-Engine plugin. Remove the abandoned dual-installation fix and source-linked Automation workaround.
- Existing workspace changes: Balloon, Dripping, ProjectToy submodules, untracked EdenGameplayExtension and Tests. Preserve all.

### Research references

- [Upstream NativeAOT branch](https://github.com/UnrealSharp/UnrealSharp/tree/b19374cee4cf25a6a98fc3e0fda4ab063b80cfdd).
- [Trimming PR 731](https://github.com/UnrealSharp/UnrealSharp/pull/731): still open/draft, not in main or nativeaot; head `28a9442be3b6184f1f269dd63a81682b7e89699d`. Introduces generated type maps, dependency annotations and a dynamic-loading feature switch. Use its design as evidence; do not silently treat it as merged or import the whole draft.
- [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/): dynamic assembly loading and runtime code generation are constrained; publishing must be validated, including warnings.
- [Native AOT interop](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop): named UnmanagedCallersOnly exports must be in the published assembly itself; referenced assemblies do not automatically contribute exports.
- [Native libraries](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries): shared-library deployment; unloading a NativeAOT library is unsupported.
- Local OpenSourcePR experience: preserve architecture and generation-time responsibilities, minimize shared-path changes, compare real generated output and separate build/runtime evidence.

## What upstream already does

1. `PackageProject -NativeAOT` first publishes managed inputs into an intermediate directory, generates a project referencing user DLLs, then publishes that aggregate project with `PublishAot`.
2. Generated `Main.cs` exposes `InitializeAotRuntime`, installs native/managed callback tables and initializes native binds.
3. Runtime host probes a project-named `.AOT.dll` and loads that export; otherwise it uses hostfxr/JIT.
4. Module initializers queue type and module-interface registration for execution after loading an assembly.
5. Plugin loading has a conditional precompiled-assembly path (`NATIVE_AOT`), and UHT-generated invocation wrappers start moving toward static entry points.

## Confirmed gaps to resolve incrementally

- Current main uses a newer UTF-8 initialization ABI with an explicit result/error buffer. Upstream's old boolean/UTF-16 entry signatures cannot overwrite it.
- Upstream still initializes hostfxr before choosing AOT, assumes `.dll`, and reuses a handle that shutdown would unload. These conflict with standalone AOT and macOS/native-library lifetime requirements.
- `UnrealSharp.AOT.props` is empty, but current main's `UnrealSharp.Build.props` already maps `BuildingAOT=true` to `NATIVE_AOT`. Preserve that newer file when resolving upstream's rename/delete conflict. Publishing inputs and the final root still need consistent target/configuration and dependency closure.
- UHT static `Invoke_*` wrappers and the existing managed dispatcher use different first-argument conventions. Both UHT and Roslyn generation, callbacks, inheritance, static methods, out/ref and exceptions must be tested together.
- Runtime type/member/constructor lookup uses reflection. Trimming/rooting and AOT constructor invocation require executable evidence, not only annotations or warning suppression.
- AOT registration can execute before callback setup. Type registration, module startup, assembly identity, cleanup and JIT reload must retain their ordering/lifetime guarantees.
- Upstream forces AOT from the Editor packaging button; preserve explicit opt-in and existing JIT packaging behavior.
- Existing test projects contain different historical fixtures. Normalize only the task's fixture inputs and document unrelated differences before claiming an A/B comparison.

## Phases and commit gates

| Step | Small deliverable | Required verification before commit |
| --- | --- | --- |
| 0 | Research, durable plan, source/test-slot manifests and preservation | Verify refs, clean fork, canonical entries, toolchain and process occupancy |
| 1 | Merge upstream/nativeaot while preserving current-main ABI and JIT compatibility | Resolve each conflict explicitly; canonical B Editor build and focused JIT startup/callback regression against A |
| 2a | AOT export/bootstrap contract | Publish minimal actual NativeAOT library; inspect export; execute entry through a native caller; verify UTF-8 and failure result |
| 2b | Portable host selection and lifetime | Load generated library in UE without hostfxr on AOT path; verify missing/export-error behavior; JIT fallback regression |
| 3a | Generated function dispatch | Static and instance UFunction, native override, inheritance; scalar return and no-argument calls in JIT and AOT |
| 3b | Function ABI boundaries | bool/name/struct, const/ref/out, string/object/container parameters, exceptions; native and managed round trips |
| 4a | Assembly/type discovery and registration order | Multiple modules, native types, user types and startup callbacks; no dynamic assembly loading in AOT |
| 4b | Object construction and lifetime | UObject wrapper and constructor/native-pointer ordering; GC handles, disposal and repeated world lifetimes |
| 4c | Property/struct/interface/delegate support | Focused fixtures per feature; each independently tested and committed |
| 5a | Target-aware AOT publish inputs and aggregate project | Game/Development macOS arm64, reproducible commands, complete dependencies, classified AOT warnings |
| 5b | Archive/staging and standalone startup | Canonical Game build then existing cook/archive flow; packaged game executes fixtures with no hostfxr/CoreCLR dependency |
| 6 | JIT Editor/hot-reload and packaged regression; documentation | Repeat only affected cases, compare artifacts and behavior, record timings as observations; final clean commits and limitations |

The sequence may be refined when an executable test reveals a prerequisite. Do not combine unrelated fixes into one commit. A failing test keeps the current step open; record it, make the smallest necessary correction, rerun the affected check, then commit.

## Acceptance and platform scope

Deliver a working, opt-in NativeAOT path on the available macOS arm64 environment, validated through actual exported calls and an Unreal runtime/game flow, with the existing JIT Editor workflow preserved. Validate representative generated functions, objects, properties, structs, interfaces, delegates, containers and module startup. Report any untested Windows/Linux/mobile/console support explicitly; this machine cannot prove those platforms. NativeAOT library hot reload/unload is not a promised feature; JIT Editor hot reload remains the development workflow.

## Progress / resume point

- Research and local inventory complete; upstream fetched.
- Plan checkpoint `9781aedd`; upstream merge `4899400f` (committed externally during validation; contents verified against deployment).
- Removed `8e1b3a7f` from local eden-main history at the user's request. Subsequent local commits were replayed; no push occurred. Removed that prerequisite from baseline A and removed the generic renamed Automation dispatcher.
- Current commits: `ca5e79d5` test infrastructure; `a9346f98` FString length fix; `d9132e0c` shared UTF-8 bootstrap/export; `c0cde37e` actual AOT core-dispatch probe. Initial source-engine evidence is preserved separately and is not Installed UE validation.
- Historical executable evidence: 22 UE JIT checks in each slot, 18 AOT export checks, 31 AOT bootstrap/dispatch checks. Keep the proven current call ABI; explicit fixture roots do not yet solve full-application trimming.
- Installed correction verified: both canonical Editor builds passed; normal UAT and both JIT probes passed (22 checks each); production-generated AOT bootstrap/dispatch passed 31 checks.
- Target-aware AOT aggregate/manifests implemented and verified against the actual published export. Portable host and shared Runtime fixture now pass 22 standalone AOT Game checks, matching baseline JIT, with no hostfxr/CoreCLR loaded. Continue broader generated features, lifetime/negative cases and JIT hot reload.
- Real Editor Hot Reload now passes 27 generated-function checks in both A and B; the same 27 pass in the standalone NativeAOT Game. Continue static UFunction reflection, broader generated features and explicit AOT lifetime/negative cases.
- Static UFunction, generated properties and custom structs are verified through UE in both JIT slots and standalone AOT: 36 identical checks. Continue containers, interfaces, delegates, modules and lifecycle/negative cases.
