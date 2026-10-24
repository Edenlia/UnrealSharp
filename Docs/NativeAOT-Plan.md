# NativeAOT implementation plan

Status: active. Updated 2026-10-24 (Asia/Shanghai).

## Outcome and scope

Deliver `feature/nativeaot` in Edenlia/UnrealSharp, preserving upstream NativeAOT history and current main fixes. Implement and exercise Windows x64 NativeAOT with UE 5.8 Installed Engine, while retaining the Editor/JIT workflow. A successful C# build alone is not acceptance: the published native library must initialize without hostfxr, register gameplay types, and execute the same observable workload as JIT inside Unreal.

NativeAOT libraries cannot be unloaded. Editor managed Hot Reload remains a JIT feature. Linux/macOS/mobile/consoles require their own toolchains and runtime verification; Windows results do not establish support for them.

## Locked inputs

- Fork: `https://github.com/Edenlia/UnrealSharp.git`.
- Base: origin/main = origin/eden-main = upstream/main = `cb8577e3a47b5bdc5454239a17f61a6282a50ae2`.
- Upstream NativeAOT: `b19374cee4cf25a6a98fc3e0fda4ab063b80cfdd` (2026-06-28), containing `a6c9f956` (2026-06-25, first unstable AOT build).
- Implementation worktree: `C:/Users/edenlia/Workspace/CodeProjects/UnrealSharp-worktrees/nativeaot`.
- A: `C:/Users/edenlia/Workspace/UEProjects/SharpTestProject`.
- B: `C:/Users/edenlia/Workspace/UEProjects/SharpTestProject1`.
- Both existing AutoBuild configurations select `C:/Program Files/Epic Games/UE_5.8`, Win64 Development. Editor and Game entry points exist. No build flags or targets will be changed.
- Both projects are outside Git. Save recoverable source/configuration/plugin snapshots before deploying any variant. Existing DuplicateTransientProbe and runtime glue are unrelated prior test content.
- Installed .NET SDKs: 9.0.315 and 10.0.400. Confirm actual SDK selection and native toolchain in build logs.

## Research findings

The upstream branch adds a generated aggregate AOT project, `InitializeAotRuntime` export, binary assembly references and trimmer roots; redirects package output through an intermediate directory; selects an AOT DLL in the native runtime host; queues type/module registration until plugin loading; and attempts static event wrappers. It is an integration prototype, not a completed supported runtime.

Current main has newer initialization ABI/error reporting, target-aware packaging, generated names, props imports and binding changes. Preserve those when resolving the merge. In particular:

- The old AOT initializer returns NativeBool and receives UTF-16. Current main receives UTF-8 and writes FCSInitializationResult. Both JIT and AOT must obey the current layout.
- Upstream initializes hostfxr before checking for the AOT DLL, and replaces the same host handle. AOT startup must not require a JIT runtime or unload its library.
- Static upstream `Invoke_*` methods take GCHandle, whereas current `InvokeManagedMethod` invokes a managed function pointer with an object argument. An adapter must preserve calling convention, GC transitions and exception handling; copying the old wrapper is invalid.
- Registration queues must preserve type-before-module startup, shutdown cleanup, repeated load and JIT Hot Reload behavior.
- Runtime reflection sites include managed method/type lookup, constructor invocation, struct marshaling, delegate invocation and exported-function discovery. Establish reachability and AOT behavior from actual publish diagnostics and runtime tests; do not suppress warnings to claim compatibility.
- Build utilities must exclude editor-only modules, honor assembly names/transitive dependencies and target configuration, root necessary metadata, stage the final native library and installed marker, and give clear failures for unsupported modes.

Primary references consulted:

- https://github.com/UnrealSharp/UnrealSharp/tree/nativeaot
- https://github.com/UnrealSharp/UnrealSharp/issues/214
- https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries

The local OpenSourcePR/README.md contribution guidance was read: keep build-time knowledge in generators where practical, limit runtime costs and unrelated changes, and validate generated outputs and runtime behavior separately.

## Phases and exit criteria

### 0. Inventory and plan

- [x] Read workspace/skill/build policies, fork state and primary NativeAOT documentation.
- [x] Fetch upstream/origin and lock refs; create isolated feature branch.
- [x] Inspect canonical AutoBuild and configuration separately for A/B.
- [x] Record upstream capabilities and known incompatibilities.
- [x] Record engine Build.version and both complete original source/configuration/plugin snapshots.

### 1. Integrate upstream

- [x] Resolve the merge against current main without reverting its UTF-8 ABI, error reporting, name resolution or target packaging fixes.
- [x] Adapt AOT entry to the same callback/result contract as JIT.
- [x] Ensure generator output and module registration agree with runtime consumers; dedicated JIT runtime smoke passed.
- [x] Validate the merged build/generator paths; record the integration commit with both parents.

### 2. Exported functions first

- [x] Trace native-to-managed and managed-to-native bind registration/call paths.
- [x] Establish whether callback replacement is necessary: existing generated NativeBinds and rooted invocation ABI pass actual AOT execution; retain that ABI and exception boundary. Fix interface wrappers to resolve the implementing object's function.
- [x] Exercise scalar/return, static and instance UFunction, inherited/overridden events, ref/out, empty parameters, string/struct/container marshalling and thrown managed exceptions.
- [ ] Compare full generated-output manifests for the affected export scope; explain all changes.
- [x] Publish and run a small actual NativeAOT export/callback probe (full AOT Unreal runtime remains pending).

### 3. Publish and runtime startup

- [x] Make NativeAOT a deliberate supported package mode, with platform and configuration validation.
- [x] Publish an aggregate native library with a rooted export and complete runtime dependencies; omit editor-only assemblies.
- [x] Select/load the native library without hostfxr, retain its process-lifetime handle, and report missing export/initialization failure precisely.
- [x] Stage the correct filename, metadata and installed marker; run the built Game against the staged output.
- [ ] Verify non-ASCII paths, missing library/export diagnostics, and ordinary JIT packages.

### 4. Runtime completeness

- [x] Verify type/module registration order and single initialization, inheritance, constructors/CDOs and default values.
- [x] Verify primitive/enums/strings/names/text, blittable/nonblittable structs, arrays/maps/sets, UObject/soft/weak references, delegates and interface dispatch.
- [ ] Verify GC handles, destruction, shutdown, exceptions, and repeat launch behavior (GC/destruction/exception checks passed; shutdown callback audit pending).
- [x] Classify every relevant IL2xxx/IL3xxx publish diagnostic; resolve reachable issues or document an enforced unsupported boundary.
- [x] Verify Editor JIT compile/Hot Reload remains functional after generated-code changes (101 -> 202 -> 101, module shutdown/startup twice, exact source restored).

### 5. A/B acceptance and delivery

- [ ] First compare base JIT (A) vs candidate JIT (B) for regressions; then candidate JIT (A) vs same candidate NativeAOT (B). Record the exact snapshot for every run. Upstream-only AOT is diagnostic, not a performance baseline if it fails.
- [ ] Verify actual plugin/assembly/library paths and versions from logs before interpreting any results.
- [ ] Same workload and result counters on both sides. Measure startup, export lookup/registration and call batches only where comparable; publish time and binary size separately.
- [ ] Functional checks precede measurements. Use sequential independent runs, warmup and alternating A-B-B-A ordering; report variance and failures.
- [ ] Preserve commands, exit codes, source/output hashes, full generated diffs, logs and raw samples in Saved/UnrealSharpAB/20261024-nativeaot and the local report folder.
- [ ] Inspect final diff, commit coherent verified steps, document supported behavior and remaining platform restrictions. Do not push without explicit authorization.

## Current resume point

Integration commit `3a3077cd` retains both main and upstream NativeAOT parents. B initially ran snapshot tree `f53c6de176acb3d5f83aad76f711f11094f95327`; its archive SHA256 is `E034464AC735C6E92841C294C65B124D0B7A802274E25DB778DF041E35D4CEF8`. A runs base `cb8577e3`, archive SHA256 `55407BDA7284A6978DEF014B46A07F264296EE3E1DCBC107044F8BD2A9A0C05A`. Original plugins and project text are under each project's `Saved/UnrealSharpAB/20261024-nativeaot/before`.

UE is 5.8.3, changelist 58210709. Candidate `AutoBuild.bat` passed (165.39 s, 225 actions); UHT/glue and managed libraries compiled. `BuildUserSolution` also passed via the plugin's official UAT command, including the new `NativeAotProbe` test module. First smoke attempts against an existing fixture exposed test-script mistakes (managed class global package/suffix and protected property access), not a proven runtime regression; logs are retained. The dedicated probe checks constructor/native property/scalar/ref-out/string/module paths.

The unsafe upstream static GCHandle event wrappers were not copied over main's object-based invocation ABI. Both event exporters retain main's implementation until a consistent adapter is implemented and measured. Main's DLL-reference helper supersedes the old branch's duplicate AddReferences implementation. The upstream forced NativeAOT Editor toolbar flag was dropped so ordinary JIT packaging remains the default.

The standalone export probe links the actual deployed NativeBinds sources and uses the deployed SourceGenerator. NativeAOT package restoration stalled; official NuGet packages were downloaded into a task-local feed and their SHA512 verified. This changes no UE native build invocation or global configuration.

Update: both official NativeAOT 10.0.11 packages were downloaded and SHA512-verified; the standalone library published successfully with SDK 10.0.400 and produced no compiler warnings. `runtime.log` records EXPORT_PROBE mask=31, lookups=4, calls=3 and REFLECTION_PROBE=1, exit 0. Actual generated native bindings cover scalar, ref/out, blittable struct, missing binding and duplicate initialization. Rooted reflection reproduces current UnrealSharp constructor and instance invocation conventions successfully. There is no evidence requiring the old upstream static GCHandle wrappers; retain the current ABI and test complete runtime reachability next.

Both A and B dedicated JIT runtime smokes passed with exit 0: module startup, constructor setting native property=11, scalar=42, native property roundtrip=123, ref=10/out=20, Unicode string roundtrip and runtime mode=JIT. See `baseline-nativeaot-probe-jit.log` and `candidate-nativeaot-probe-jit.log`. The first A build encountered another user's global UBT log lock; it was retried unchanged after that build completed. The canonical retry passed in 147.54 seconds.

Phase 3 is in progress. The candidate now builds its AOT aggregate from just-published Game assemblies, roots load-order metadata, stages an explicit AOT marker, selects AOT before hostfxr, and permits embedded assemblies without IL DLL files on disk. `candidate-phase3-overlay.json` identifies these uncommitted deployed sources. Canonical B `AutoBuildGame.bat` build 3 passed in 25.99 seconds and compiled the changed runtime sources. The first Game build exposed a test harness API error and pre-existing fixture calls to modular-only GetModuleFilename; test-only fixes preserve Editor behavior and report the executable in monolithic Games. A deployment helper was stopped before source writes when an earlier build started; no source deployment was allowed to overlap it. Copy-preserved timestamps required re-writing changed source bytes before build 3, which is the authoritative phase 3 native build.

Full `PackageProject -NativeAOT` publication and first Game smoke passed. The 118,184,960-byte native library initialized with dynamic code disabled, executed the initial constructor/property/scalar/ref-out/Unicode workload and exited 0; sampled modules included the AOT library, not hostfxr/coreclr. The reused old map reported a missing historical test actor after the smoke, so it is not clean map acceptance. A new empty `/Game/NativeAotValidation/Validation` was created through Editor Python and is being cooked through UAT with native building disabled.

Expanded JIT coverage passed all ten groups (mask 1023), static functions, inheritance and a native ReceiveTick override. It exposed an existing interface-wrapper bug: non-event interface methods used the declaration UFunction rather than the implementing object's function. The generator now caches the instance function for every interface wrapper; parameterless void/return and parameterized calls pass. A first test script failed only because TickValue was not Python-readable; the fixture property was corrected and the complete rerun passed. NativeAOT must still run the expanded matrix, native GC and exception boundary.

Real Editor managed Hot Reload passed 101 -> 202 -> 101 with exactly matching source bytes after restoration, and module shutdown/startup callbacks appeared for both reloads. No native Live Coding build was requested (the Editor's Live Coding server emitted unrelated startup indexing errors). AOT now explicitly rejects unload/Hot Reload and excludes its unused dynamic assembly-load context; that AOT-only boundary still needs the next publish/run.

Detailed trim diagnostics are retained without suppression. Reflection sites are covered by whole-assembly roots for runtime/glue/user load-order assemblies. AOT packaging also preserves native dependencies and user-published content from the intermediate target publish; an independent native export and UTF-8 content fixture will verify this path. Original source checkout remains on feature/csharp-stage; nothing has been pushed.

Expanded NativeAOT Game acceptance passed in a Unicode archive path: ten groups/mask 1023, native event override 125, static/inherited results 42, UTF-8 content/native dependency 42, native GC collected the UObject and invoked managed Dispose once. Separate controlled exception run returned to native and completed the following call. Both exited 0. Missing native DLL and missing InitializeAotRuntime were independently rejected with exit 3 and exact diagnostics; the original AOT DLL was restored and SHA256-verified after each test. Editor and Linux AOT package options were rejected before publishing. AOT publish 4 excludes PluginLoadContext warnings and retains actionable reflection diagnostics, classified in NativeAOT.md. Final same-candidate JIT/AOT comparison remains in progress.
