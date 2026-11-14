# NativeAOT packaging

NativeAOT is an opt-in package mode for non-Editor Win64 x64 and Mac arm64 targets on the matching host OS. iOS arm64 packages built on macOS always use NativeAOT. Editor development continues to use JIT and managed Hot Reload. Build and runtime verification is required for each target, configuration and dependency set.

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
- Generated console-object initializers queue registration after native bindings and type registration, before module startup. AOT console objects live for the process lifetime and do not query the calling assembly for unload ownership; Editor JIT keeps assembly ownership and Hot Reload cleanup.
- NativeAOT assemblies and their native library remain loaded for the process lifetime. Unload requests are rejected before changing registry, module or handle state. Republish and restart the game to load changes. JIT Editor Hot Reload remains available.
- Published assemblies are referenced explicitly, and runtime plus manifest assemblies are conservatively rooted for reflection. Publish warnings remain visible. This preserves the existing reflection-based constructors, invocation thunks, structs, interfaces and delegates; it does not claim fine-grained trimming or arbitrary dependency compatibility.
- Runtime code generation and loading additional managed assemblies are unsupported by NativeAOT. Third-party packages must satisfy its restrictions. Embedded assemblies have no usable `Assembly.Location`; locate published content through `AppContext.BaseDirectory`.
- Existing Game shutdown semantics are retained. Do not rely on managed `IModuleInterface.ShutdownModule` being called at process exit; use the appropriate Unreal lifecycle hook for required gameplay cleanup.

## iOS packaging

Use the project's canonical iOS build/package entry with this source engine. The UnrealSharp staging handler selects NativeAOT for IOS receipts and publishes the actual Game/Client target glue and user assemblies. Simulator, tvOS, and Mac Catalyst targets are not supported by this integration.

The aggregate library targets `net10.0` with RID `ios-arm64` and `PublishAotUsingRuntimePack=true`. This uses the .NET NativeAOT runtime pack without requiring the .NET iOS workload. Xcode's iOS platform support and a usable development signing identity/provisioning profile are required for device packaging.

The publisher converts the shared library into `UnrealSharpAOT.framework`, setting its install name to `@rpath/UnrealSharpAOT.framework/UnrealSharpAOT`. After successful publication, the staging handler sets the process-local `UNREALSHARP_IOS_FRAMEWORKS` directory for `UnrealSharpCore.Build.cs`. The subsequent UAT run-only Xcode project generation declares every framework in it through `PublicAdditionalFrameworks` with `FrameworkMode.Copy`. UE's existing Embed Frameworks phase copies it into the app's `Frameworks` directory and uses `CodeSignOnCopy` before signing the app. No Engine source modification or separate signing script is required.

The native build's earlier PostBuildSync does not receive that path, so a first build does not require an existing AOT framework. Staging always publishes before declaring the framework and fails if publication fails; it does not select a previous output by checking for its existence. The path belongs to the publishing receipt's project, target, configuration and architecture. This matters because UE discovers Xcode frameworks using Development module rules even when packaging another configuration. Use the full UAT staging/package workflow; a native build alone does not publish managed game code. Do not place the executable inside a pak or modify it after signing. Native debug symbols remain in the intermediate publish directory and are not deployed as managed content.

Load-order manifests and the AOT marker follow the normal managed-data staging path. At startup, iOS loads the framework from the physical app bundle with `dlopen`/`dlsym`; Unreal's generic DLL methods are not implemented for iOS. Managed file APIs receive the physical bundle data directory. A missing marker or framework is a startup failure; iOS never falls back to hostfxr/JIT.

See Microsoft's [custom NativeAOT frameworks for iOS](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/ios-like-platforms/creating-and-consuming-custom-frameworks) for the runtime-pack and framework requirements. Packaging success alone does not establish device runtime or gameplay compatibility.

References: [.NET NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/), [root-assembly exports](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop), [native-library lifetime](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries).
