# NativeAOT packaging

NativeAOT is an opt-in package mode for non-Editor Win64 x64 and Mac arm64 targets on the matching host OS. Editor development continues to use JIT and managed Hot Reload. Build and runtime verification is required for each target, configuration and dependency set.

## Publishing

Build the native game and cook/stage its archive using the project's normal Unreal build and packaging workflow. Then run UnrealSharp's `PackageProject` UAT command with `-NativeAOT`, for example:

```text
PackageProject
  -Project=<ProjectDirectory>/<Project>.uproject
  -ScriptDir=<ProjectDirectory>
  -ScriptsForProject=<ProjectDirectory>/<Project>.uproject
  -ArchiveDirectory=<PackagedProjectDirectory>
  -UETargetType=Game -UEBuildConfig=Development
  -TargetPlatform=Mac -TargetArchitecture=arm64 -NativeAOT
```

Use the engine's normal UAT launcher and quote paths as required by the shell. `ArchiveDirectory` is the project directory inside the package, where `Binaries/Managed/net10.0` belongs. For a Mac app this is `<Project>.app/Contents/UE/<Project>`. For Windows use `TargetPlatform=Win64` and `TargetArchitecture=x64`. Publishing requires .NET 10 and the platform's NativeAOT compiler prerequisites.

The command publishes runtime, glue and user assemblies for the requested target into `Intermediate/UnrealSharp/NativeAOT`. It then creates a separate aggregate project referencing those exact assemblies. This avoids importing Editor output into the final native library. Editor-only references in user projects must be conditional on `UETargetType=Editor`.

The final managed output is replaced with the native library, load-order manifests, published user content and native dependencies. `UnrealSharp.NativeAOT` is written only after `<Project>.AOT.dll` or `<Project>.AOT.dylib` exists. Preserve existing package payloads before comparing package modes. Omitting `-NativeAOT` keeps ordinary self-contained JIT packaging.

## Runtime and compatibility

- The non-Editor host checks the AOT marker before initializing hostfxr. A marked package with a missing or invalid library, missing `InitializeAotRuntime` export, or failed initialization is an error; it does not fall back to JIT. An unmarked package uses JIT.
- The generated root assembly exports the same UTF-8 initialization/callback/result contract as JIT. Type registration runs before module startup, after native callbacks are available.
- NativeAOT assemblies and their native library remain loaded for the process lifetime. Unload requests are rejected before changing registry, module or handle state. Republish and restart the game to load changes. JIT Editor Hot Reload remains available.
- Published assemblies are referenced explicitly, and runtime plus manifest assemblies are conservatively rooted for reflection. Publish warnings remain visible. This preserves the existing reflection-based constructors, invocation thunks, structs, interfaces and delegates; it does not claim fine-grained trimming or arbitrary dependency compatibility.
- Runtime code generation and loading additional managed assemblies are unsupported by NativeAOT. Third-party packages must satisfy its restrictions. Embedded assemblies have no usable `Assembly.Location`; locate published content through `AppContext.BaseDirectory`.
- Existing Game shutdown semantics are retained. Do not rely on managed `IModuleInterface.ShutdownModule` being called at process exit; use the appropriate Unreal lifecycle hook for required gameplay cleanup.

References: [.NET NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/), [root-assembly exports](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop), [native-library lifetime](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries).
