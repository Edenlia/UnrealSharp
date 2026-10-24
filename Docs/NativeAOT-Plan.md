# NativeAOT implementation plan

Status: completed for Windows x64 Development Game. Updated 2026-10-24 (Asia/Shanghai).

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
- [x] Compare full generated-output manifests for the affected export scope; explain all changes.
- [x] Publish and run a small actual NativeAOT export/callback probe (followed by full AOT Unreal runtime acceptance).

### 3. Publish and runtime startup

- [x] Make NativeAOT a deliberate supported package mode, with platform and configuration validation.
- [x] Publish an aggregate native library with a rooted export and complete runtime dependencies; omit editor-only assemblies.
- [x] Select/load the native library without hostfxr, retain its process-lifetime handle, and report missing export/initialization failure precisely.
- [x] Stage the correct filename, metadata and installed marker; run the built Game against the staged output.
- [x] Verify non-ASCII paths, missing library/export diagnostics, and ordinary JIT packages.

### 4. Runtime completeness

- [x] Verify type/module registration order and single initialization, inheritance, constructors/CDOs and default values.
- [x] Verify primitive/enums/strings/names/text, blittable/nonblittable structs, arrays/maps/sets, UObject/soft/weak references, delegates and interface dispatch.
- [x] Verify GC handles, destruction, clean process shutdown, exceptions and repeat launches; document the inherited Game module-shutdown callback behavior.
- [x] Classify every relevant IL2xxx/IL3xxx publish diagnostic; resolve reachable issues or document an enforced unsupported boundary.
- [x] Verify Editor JIT compile/Hot Reload remains functional after generated-code changes (101 -> 202 -> 101, module shutdown/startup twice, exact source restored).

### 5. A/B acceptance and delivery

- [x] First compare base JIT (A) vs candidate JIT (B) for regressions; then candidate JIT (A) vs same candidate NativeAOT (B). Record the exact snapshot for every run. Upstream-only AOT is diagnostic, not a performance baseline if it fails.
- [x] Verify actual plugin/assembly/library paths and versions from logs before interpreting any results.
- [x] Same workload and result counters on both sides. Record comparable call batches and full-process wall time; explicitly distinguish these from unmeasured isolated startup/export-registration time. Publish time and binary size separately.
- [x] Functional checks precede measurements. Use sequential independent runs, warmup and alternating A-B-B-A ordering; report variance and failures.
- [x] Preserve commands, exit codes, source/output hashes, full generated diffs, logs and raw samples in Saved/UnrealSharpAB/20261024-nativeaot and the local report folder.
- [x] Inspect final diff, commit coherent verified steps, document supported behavior and remaining platform restrictions. Do not push without explicit authorization.

## Completion record

Runtime snapshot `d95435db` passed final acceptance in both projects. Integration merge `3a3077cd` preserves upstream history and the current initialization ABI; `c25edd75` fixes interface dispatch exposed by the matrix. Final deployments match all 802 build-related source files after line-ending normalization. The final generated Game comparison contains 15,239 files per side with zero byte differences.

The NativeAOT Game loads without hostfxr/coreclr, passes the ten-group runtime matrix, native UObject collection/disposal, event override, UTF-8 content/native dependency and controlled exception tests. Missing DLL/export and unsupported package modes fail explicitly. JIT Game passes the same workload. Real Editor C# Hot Reload passes 101 → 202 → 101 with exact source restoration.

Twelve sequential A–B–B–A samples pass all result counters. The measured calls are slightly slower under AOT in this fixture; the current rooted payload is not smaller. No general performance improvement is claimed. Full process wall time includes shutdown and is not isolated startup latency.

See `NativeAOT-Validation.md` for the complete acceptance matrix, measurements, hashes/diff locations and retained failure history; `NativeAOT.md` documents usage and boundaries. The existing native Game shutdown path does not invoke managed module ShutdownModule; JIT unload does. No new Game shutdown-callback behavior is claimed.

Remaining expansion work is outside this completed Windows Development Game scope: validate Shipping/Client/Server and other platforms with their own canonical native workflows; evaluate narrower generated roots/export tables if size or startup requirements justify them; audit each third-party dependency for NativeAOT restrictions. NativeAOT unload/Hot Reload remains prohibited by the runtime contract. The local feature branch is ready for review; no remote push or PR was performed.
