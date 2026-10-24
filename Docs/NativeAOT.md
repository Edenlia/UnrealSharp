# NativeAOT packaging

This branch adds an opt-in NativeAOT package path for Windows x64. Development Game is the initial validation target. Editor stays on the normal .NET runtime, including C# Hot Reload. Other platforms and Client/Server targets need separate runtime validation.

## Build and package

Use the project's normal native build entry points for the selected Engine, target, platform and configuration. Native Game and Editor binaries must be built successfully before their corresponding verification or cook step. Do not run native compilation while an Editor or Game holds the same outputs.

Cook and stage the Game with Unreal's normal packaging tools. If native binaries were built separately, disable native building in that packaging invocation. Then run the plugin's `PackageProject` UAT command, adding `-NativeAOT`:

```text
RunUAT.bat PackageProject
  -ScriptDir=<ProjectDirectory>
  -ScriptsForProject=<ProjectDirectory>/<Project>.uproject
  -Project=<ProjectDirectory>/<Project>.uproject
  -ArchiveDirectory=<Archive>/Windows/<Project>
  -UETargetType=Game -UEBuildConfig=Development
  -TargetPlatform=Win64 -TargetArchitecture=x64 -NativeAOT
```

The archive directory is the project directory inside the packaged build, where `Binaries/Managed/net10.0` belongs. Use proper shell quoting for paths containing spaces. NativeAOT publishing requires the .NET 10 SDK and its Windows native compiler prerequisites on the build machine. The packaged AOT runtime does not require hostfxr or a separately installed .NET runtime.

The command first publishes the target's runtime, generated glue and user projects into `Intermediate/UnrealSharp/NativeAOT`. It then generates an aggregate shared-library project and publishes `<Project>.AOT.dll`. Explicit references to already-published target assemblies prevent an Editor DLL from replacing its Game counterpart. Native dependencies and user content from that publish are staged alongside the library. Editor-only references in user project files must be conditional on `UETargetType=Editor`.

The final directory contains the native DLL, load-order manifests, `UnrealSharpBuild.flag`, and `UnrealSharp.NativeAOT`. The AOT marker is written only after the expected native library exists. The native host checks the marker before creating hostfxr; a missing DLL or `InitializeAotRuntime` export is an error, not a silent JIT fallback. An ordinary package without `-NativeAOT` uses the existing JIT path.

## Runtime contract

The native entry point uses the same UTF-8 working-directory argument, callbacks and `FCSInitializationResult` layout as current JIT initialization. Native binds use the existing generated function-pointer registration. Type registration and module startup are queued until the corresponding assembly is loaded, so NativeAOT's early module initializers cannot call native services before initialization.

The native library is resident for the process lifetime. NativeAOT does not support library unloading, dynamic managed assembly loading or managed Hot Reload. Those paths are explicitly unavailable in AOT; restart the process after publishing a new version. Editor C# Hot Reload continues to use collectible load contexts.

This implementation uses whole-assembly roots for the UnrealSharp runtime and every glue/user assembly in the load-order manifests. Reflection metadata is required for constructors, invocation thunks, managed structs, interfaces and delegate wrappers. It does **not** claim arbitrary trimming compatibility or automatically make third-party reflection/dynamic-code libraries AOT-compatible. Dependencies must satisfy .NET NativeAOT's restrictions; review their publish diagnostics.

## Publish diagnostics

The aggregate sets `TrimmerSingleWarn=false`, preserving individual diagnostics. Warnings are not suppressed to make the build appear compatible.

| Path | Interpretation and requirement |
| --- | --- |
| `UnrealSharpObject.Create`, `UnmanagedCallbacks` type/method/struct lookup | Types arrive through native handles or names, which static analysis cannot follow. Their declaring UnrealSharp/glue/user assemblies are rooted. Runtime tests must exercise these paths. |
| `TDelegateBase<T>` wrapper lookup/construction | Generated wrapper types are found by name in the delegate's assembly. That assembly must be part of the rooted gameplay/glue set. |
| Legacy `ExportedFunctionsManager` | Whole-runtime rooting retains this older reflection API. It is not used by the current generated `NativeBinds` bootstrap. |
| User `Assembly.Location` calls | NativeAOT embeds managed assemblies, so these paths are empty. Use `AppContext.BaseDirectory` for published content; use the native host log for the loaded AOT DLL path. |
| Dynamic assembly-load APIs | The AOT build excludes `PluginLoadContext` and rejects unload/Hot Reload. Do not depend on runtime discovery of new managed DLLs. |

NativeAOT support is established by a published Game run, not by a successful C# build. See `NativeAOT-Plan.md` for the current validation status and `Tests/NativeAOT` for the reproducible workload.

Primary references: [upstream prototype](https://github.com/UnrealSharp/UnrealSharp/tree/nativeaot), [.NET NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/), [native libraries and lifetime](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries).
