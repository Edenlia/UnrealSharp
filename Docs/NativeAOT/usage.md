# NativeAOT on eden-main

## Validated configuration

The implemented path is opt-in packaging on macOS arm64 with .NET SDK 10.0.401 / runtime 10.0.12 and Installed UE 5.8.3 (CL 58210709). Editor development continues to use JIT and normal C# Hot Reload. Windows, Linux, mobile, consoles, Shipping and other engine/runtime versions require their own builds and runtime validation; this work does not claim they passed.

Both test projects use `/Users/Shared/Epic Games/UE_5.8` and a project-local UnrealSharp plugin. The source Engine checkout is not their build/runtime engine. The abandoned dual-plugin-selection fix was removed from eden-main; no Engine installation changes are needed.

## Build and package

1. Use the target project's existing, unchanged AutoBuild entry for Editor/Game native builds. Wait for a successful exit before cooking or running. Close processes using those outputs before native compilation. The validation runner enforces source hashes against the successful build.
2. Cook/stage/archive the native game using the project's established packaging workflow. The test runner's `archive` operation uses `skipbuild`, `skipbuildeditor` and `nocompileeditor`; it cannot replace a successful canonical native build.
3. Run the ordinary UnrealSharp UAT `PackageProject` command for that archive, with explicit UE target/configuration/platform/architecture and `-NativeAOT`. On Mac, ArchiveDirectory points inside the app at `Contents/UE/<Project>`, not at the enclosing archive directory.

For the existing B test slot, from the UnrealSharp repository, after creating a fresh archive named `<archive-round>`:

```sh
node Tests/NativeAOT/run.mjs uat B <unique-publish-round> PackageProject \
  -ArchiveDirectory=/Users/shuyangzhang/Workspace/UnrealEngine/Tests/NativeAOT/eden-main/archives/<archive-round>/Mac/SharpTestProject1.app/Contents/UE/SharpTestProject1 \
  -UETargetType=Game -UEBuildConfig=Development -TargetPlatform=Mac -TargetArchitecture=arm64 -NativeAOT
node Tests/NativeAOT/run.mjs game B <unique-runtime-round> <archive-round>
```

Replace angle-bracket placeholders before executing. Every evidence round must be unique. See [test instructions](../../Tests/NativeAOT/README.md) for preservation, deployment, canonical build and Hot Reload commands.

The managed package contains `<Project>.AOT.dylib`, the user/glue load-order manifests and `UnrealSharpBuild.flag`. The final aggregate references and roots every manifest assembly, including native glue that user code may not directly reference. Target/configuration apply to both its inputs and the final root. The actual native export is `InitializeAotRuntime` in the root assembly.

Without `-NativeAOT`, PackageProject publishes the ordinary self-contained JIT payload. It replaces the managed output directory, so preserve an existing test payload before comparing package modes. JIT and AOT are alternative payloads for the same native game; a present but broken AOT library is a fatal configuration error and never silently falls back to JIT.

## Runtime contract and limits

- Non-Editor startup chooses AOT before initializing hostfxr. Missing AOT selects the existing JIT path. The initialization ABI is the shared UTF-8 directory/result structure and existing plugin/managed callback tables.
- The AOT shared library remains loaded until process exit. Its logical assemblies cannot be hot reloaded or unloaded. The native unload callback rejects without removing modules or handles; direct managed unload throws before mutation. Republish/restart the game when code changes.
- Generated type/module registration runs after callbacks are installed. Existing MethodHandle/calli dispatch is retained and tested through actual generated UFunctions, including interface implementations, delegate parameters and exceptions.
- Common JIT defects found by A/B expansion were fixed separately: retained FString length, interface dispatch resolution, delegate parameter addressing/caching, and native parameter initialization/destruction. Baseline A carries these same prerequisites for comparison.
- Whole-container property assignment remains unsupported and throws NotSupportedException. Use in-place array/set/map operations. Container function inputs, ref/out values and returns are covered.
- NativeAOT cannot support arbitrary runtime code generation or loading new managed DLLs into the process. Third-party managed dependencies still need their own AOT-compatible implementation and runtime checks.

## Trimming and evidence

Whole-assembly roots are a conservative compatibility measure. Eleven reflection warnings remain visible in the tested package: seven core type/method/constructor lookup sites, two delegate wrapper sites, and two legacy exported-function reflection sites. They are not suppressed, and successful representative tests do not prove every reflective API/dependency combination. Fine-grained trimming, package-size optimization and comparative performance/memory benchmarks are separate work.

[Validation history](validation.md) records small commits, positive and failed experiments, exact evidence rounds and scope. Evidence includes command/native-source manifests, fixture snapshots, full build/runtime logs, PASS names, loaded-image checks and negative-test payload restoration hashes. Positive functional runs must have the expected report; exit code alone is insufficient on macOS.

References: [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/), [interop exports](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop), [native library lifetime](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries).
