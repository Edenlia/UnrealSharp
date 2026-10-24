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
- [x] Record engine Build.version and B source/configuration/plugin snapshots (A still pending).

### 1. Integrate upstream

- [x] Resolve the merge against current main without reverting its UTF-8 ABI, error reporting, name resolution or target packaging fixes.
- [x] Adapt AOT entry to the same callback/result contract as JIT.
- [x] Ensure generator output and module registration agree with runtime consumers; dedicated JIT runtime smoke passed.
- [x] Validate the merged build/generator paths; record the integration commit with both parents.

### 2. Exported functions first

- [ ] Trace native-to-managed and managed-to-native bind registration/call paths.
- [ ] Replace unsupported/inconsistent callback lookup with explicit generated registration where necessary, preserving the JIT exception boundary.
- [ ] Exercise scalar/return, static and instance UFunction, inherited/overridden events, ref/out, empty parameters, string/struct/container marshalling and thrown managed exceptions.
- [ ] Compare full generated-output manifests for the affected export scope; explain all changes.
- [ ] Run a small actual NativeAOT export/callback probe, then the same workload in Unreal through canonical builds.

### 3. Publish and runtime startup

- [ ] Make NativeAOT a deliberate supported package mode, with platform and configuration validation.
- [ ] Publish an aggregate native library with a rooted export and complete runtime dependencies; omit editor-only assemblies.
- [ ] Select/load the native library without hostfxr, retain its process-lifetime handle, and report missing export/initialization failure precisely.
- [ ] Stage the correct filename, metadata and installed marker; run the built Game against the staged output.
- [ ] Verify non-ASCII paths, missing library/export diagnostics, and ordinary JIT packages.

### 4. Runtime completeness

- [ ] Verify type/module registration order and single initialization, inheritance, constructors/CDOs and default values.
- [ ] Verify primitive/enums/strings/names/text, blittable/nonblittable structs, arrays/maps/sets, UObject/soft/weak references, delegates and interface dispatch.
- [ ] Verify GC handles, destruction, shutdown, exceptions, and repeat launch behavior.
- [ ] Classify every relevant IL2xxx/IL3xxx publish diagnostic; resolve reachable issues or document an enforced unsupported boundary.
- [ ] Verify Editor JIT compile/Hot Reload remains functional after generated-code changes.

### 5. A/B acceptance and delivery

- [ ] First compare base JIT (A) vs candidate JIT (B) for regressions; then candidate JIT (A) vs same candidate NativeAOT (B). Record the exact snapshot for every run. Upstream-only AOT is diagnostic, not a performance baseline if it fails.
- [ ] Verify actual plugin/assembly/library paths and versions from logs before interpreting any results.
- [ ] Same workload and result counters on both sides. Measure startup, export lookup/registration and call batches only where comparable; publish time and binary size separately.
- [ ] Functional checks precede measurements. Use sequential independent runs, warmup and alternating A-B-B-A ordering; report variance and failures.
- [ ] Preserve commands, exit codes, source/output hashes, full generated diffs, logs and raw samples in Saved/UnrealSharpAB/20261024-nativeaot and the local report folder.
- [ ] Inspect final diff, commit coherent verified steps, document supported behavior and remaining platform restrictions. Do not push without explicit authorization.

## Current resume point

Merge conflicts are resolved, staged and awaiting the first integration commit. B runs snapshot tree `f53c6de176acb3d5f83aad76f711f11094f95327`; its archive SHA256 is `E034464AC735C6E92841C294C65B124D0B7A802274E25DB778DF041E35D4CEF8`. Subsequent source changes so far only remove trailing whitespace and update this plan. Original B plugin and project text are under B's `Saved/UnrealSharpAB/20261024-nativeaot/before`.

UE is 5.8.3, changelist 58210709. Candidate `AutoBuild.bat` passed (165.39 s, 225 actions); UHT/glue and managed libraries compiled. `BuildUserSolution` also passed via the plugin's official UAT command, including the new `NativeAotProbe` test module. First smoke attempts against an existing fixture exposed test-script mistakes (managed class global package/suffix and protected property access), not a proven runtime regression; logs are retained. The dedicated probe checks constructor/native property/scalar/ref-out/string/module paths.

The unsafe upstream static GCHandle event wrappers were not copied over main's object-based invocation ABI. Both event exporters retain main's implementation until a consistent adapter is implemented and measured. Main's DLL-reference helper supersedes the old branch's duplicate AddReferences implementation. The upstream forced NativeAOT Editor toolbar flag was dropped so ordinary JIT packaging remains the default.

The standalone export probe links the actual deployed NativeBinds sources and uses the deployed SourceGenerator. NativeAOT package restoration stalled; an official NuGet package was downloaded into a task-local feed and its SHA512 verified. This changes no UE native build invocation or global configuration. Complete the dependency restore, publish and invoke the real native export before drawing an AOT compatibility conclusion.

B's dedicated runtime smoke passed with exit 0: module startup, constructor setting native property=11, scalar=42, native property roundtrip=123, ref=10/out=20, Unicode string roundtrip and runtime mode=JIT. See `candidate-nativeaot-probe-jit.log`. This is an integration checkpoint, not NativeAOT acceptance. Next: finish the standalone NativeAOT export probe and establish the A base-JIT regression comparison before evolving NativeAOT packaging. Original source checkout remains on feature/csharp-stage.
