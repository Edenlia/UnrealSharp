# Runtime hosts

UnrealSharp brings managed code into a process through a *runtime host*: a `FCSRuntimeHost` subclass in
`Source/UnrealSharpCore/Private/Runtime/` that starts its runtime and resolves the managed `InitializeUnrealSharp`
entry point. Everything after that (calling the entry point, reporting managed exceptions,
`-waitformanageddebugger`) is shared in `FCSRuntimeHost::Initialize`.

| Kind | Host | Runtime | Platforms | Typical use |
|---|---|---|---|---|
| `CoreClrHostFxr` | `FCSHostFxrRuntimeHost` | CoreCLR started through hostfxr (shared or self-contained .NET) | Win64, Mac | Editor, desktop JIT packages |
| `CoreClrEmbedded` | `FCSEmbeddedCoreClrRuntimeHost` | CoreCLR started through `coreclr_initialize` | Mac, Android | Android (JIT); Mac exercises the host on the desktop |
| `NativeAot` | `FCSNativeAotRuntimeHost` | A NativeAOT shared library; there is no separate runtime | Win64, Mac, iOS, Android | Shipping builds without a JIT |

A .NET runtime can only be started once per process and cannot be restarted, so a host is initialized once and
lives until the process exits. Hot reload happens inside the runtime through collectible `AssemblyLoadContext`s.

## Capabilities

Code outside the hosts should ask what the active runtime can do instead of which runtime it is:

```cpp
const FCSRuntimeCapabilities Capabilities = FUnrealSharpCoreModule::Get().GetRuntimeCapabilities();
```

| Capability | CoreClrHostFxr | CoreClrEmbedded | NativeAot |
|---|---|---|---|
| `bCanLoadAssembliesFromPath` | yes | yes | no |
| `bSupportsCollectibleLoadContexts` | yes | no (ReadyToRun code is ignored in collectible contexts) | no |
| `bSupportsHotReload` | Editor only | no | no |
| `bAssembliesArePrelinked` | no | no | yes |

`IsNativeAOT()` remains as a deprecated forwarder to `bAssembliesArePrelinked`.

## How the host is chosen

`RuntimeHostFactory::CreateRuntimeHost` decides:

1. The Editor always uses `CoreClrHostFxr`; it builds and hot reloads managed code.
2. A packaged build reads `UnrealSharp.Runtime.json` from its managed directory (`Binaries/Managed/net10.0`).
3. Without a manifest, an `UnrealSharp.NativeAOT` marker (written by older packaging) means `NativeAot`.
4. Without either, platforms that compile the hostfxr host fall back to it; other platforms fail with an error.

Which hosts exist in a build is decided at compile time by `UnrealSharpCore.Build.cs`:
`UNREALSHARP_WITH_HOSTFXR_HOST` (not iOS or Android), `UNREALSHARP_WITH_NATIVEAOT_HOST` (all),
`UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST` (iOS, Mac and Android; the ReadyToRun loader reads Mach-O images, and
Android hands assemblies to the runtime through `external_assembly_probe`).

### The runtime manifest

Packaging writes the manifest; `FCSRuntimeManifest` reads it.

```json
{
  "Version": 1,
  "Kind": "CoreClrEmbedded",
  "Library": "libcoreclr.dylib",
  "AssemblySource": "Files",
  "Properties": { "RUNTIME_IDENTIFIER": "osx-arm64", "System.GC.Concurrent": "false" },
  "NativeLibraries": { "libSystem.Native": "libSystem.Native.dylib" },
  "ReadyToRunImages": { "MyGame.CoreCLR.r2r.dylib": "MyGame.CoreCLR.r2r.dylib" }
}
```

Library references are platform relative (`PlatformUtilities::ResolveBundledLibraryPath`): relative to the managed
directory on desktop, to the app's `Frameworks` directory on iOS, and a bare file name on Android.
`AssemblySource` (CoreClrEmbedded only) is `Files` when omitted: the assemblies are files in the managed directory.
`Probe` means the host reads them through the Unreal file system and returns them from `external_assembly_probe`.

## Choosing the runtime when packaging

The packaging scripts resolve a `RuntimeFlavor` (same names as the runtime kinds), in this order:

1. `-Runtime=<Kind>` on `PackageProject` (`-NativeAOT` is an alias for `-Runtime=NativeAot`);
2. the `"Runtime"` setting in `Config/UnrealSharp.Settings.json`, either a string or an object keyed by platform
   with an optional `"Default"`;
3. the legacy `"NativeAOT": true` setting;
4. the platform default: `CoreClrHostFxr` on Win64 and Mac, `NativeAot` on iOS and Android.

```json
{
  "Runtime": { "Default": "CoreClrHostFxr", "Android": "CoreClrEmbedded" },
  "CoreClrEmbeddedDotNetRoot": "/path/to/dotnet11"
}
```

Each platform lists the runtimes it can package (`PlatformUtilities.SupportedRuntimes`); asking for another fails
before anything is built.

## CoreClrEmbedded in detail

`CoreClrEmbedded` starts CoreCLR without hostfxr, for platforms that do not ship it. Platforms decide
(`PlatformUtilities`) whether they can publish the runtime, the RID, whether to build a ReadyToRun composite and in
which container; Apple platforms use `macho` and require the composite.

Packaging (`CoreClrEmbeddedUtilities`) is a second stage, like NativeAOT:

1. The normal publish produces framework-dependent IL for the target RID.
2. A generated `<Project>.CoreCLR.csproj` (net11.0) republishes it self-contained with the .NET 11 SDK
   (`"CoreClrEmbeddedDotNetRoot"`, `UNREALSHARP_DOTNET11_ROOT`, or the default installation). It trims only the
   framework (`TrimMode=partial`) and compiles it into one ReadyToRun composite with
   `PublishReadyToRunContainerFormat=macho`, which the SDK links into a dylib exporting `RTR_HEADER`. The
   `"CoreClrEmbeddedReadyToRun"` setting chooses what else goes into the composite:
   - `"ExcludeEngineBindings"` (default): UnrealSharp and user assemblies, except the generated engine bindings
     (`UnrealSharp.dll`), which stay IL and run on the interpreter (JIT on Mac);
   - `"All"`: every assembly, including the engine bindings.

   For the test project the composite is about 21 MB with the default and 345 MB with `"All"`; the engine bindings
   alone account for about 320 MB of native code.
3. The platform arranges the result. Mac keeps the desktop layout: `libcoreclr`, the runtime's native libraries and
   the composite stay next to the managed assemblies.

### Android

Android runs CoreCLR with its JIT. The publish uses the `android-<arch>` RID, whose runtime pack is CoreCLR without
hostfxr (`linux-bionic` stays the NativeAOT RID), and builds no ReadyToRun composite; the Android workload is not
needed.

- `libcoreclr`, `libclrjit`, the debugger libraries and the runtime's native libraries are copied into `Native/`,
  which UPL packages as the APK's native libraries.
- The assemblies are staged into the pak like other managed files, and the manifest says `"AssemblySource": "Probe"`.
- `libSystem.Security.Cryptography.Native.Android` calls into Java: its jar goes into `Java/`, and UPL adds the jar
  and loads the library with `System.loadLibrary` so its `JNI_OnLoad` runs.

At startup `FCSEmbeddedCoreClrRuntimeHost`:

- loads `libcoreclr` and each ReadyToRun composite (recording its `RTR_HEADER` and Mach-O image extent);
- passes every assembly in the managed directory as `TRUSTED_PLATFORM_ASSEMBLIES`, plus `APP_PATHS`,
  `NATIVE_DLL_SEARCH_DIRECTORIES`, `SYSTEM_CORELIB_DIRECTORY` and the manifest's runtimeconfig properties;
- installs a `host_runtime_contract` (`HOST_RUNTIME_CONTRACT`) that binds p/invokes such as `libSystem.Native` to the
  packaged libraries and hands the runtime the native ReadyToRun code for component assemblies;
- sets `UnrealSharp.UseAssemblyDependencyResolver=false`, because `AssemblyDependencyResolver` needs hostpolicy;
  plugins then bind by name from the trusted platform assemblies;
- resolves `UnrealSharp.Plugins.Main.InitializeUnrealSharp` with `coreclr_create_delegate`.

The hosting headers (`coreclrhost.h`, `host_runtime_contract.h`) are private dotnet/runtime interfaces, vendored from
the v11.0.0 tag in `Managed/DotNetRuntime/inc`. A newer runtime may need them refreshed.

### Known limitations

- Engine binding calls are not precompiled by default, which trades speed for about 320 MB of package size. Use
  `"CoreClrEmbeddedReadyToRun": "All"` when that speed matters more than size.
- On Mac the process aborts when a thread exits during shutdown. The .NET 11 `libclrjit.dylib` keeps a private
  `operator delete(void*)` but imports `operator new(size_t, nothrow_t)` from libc++. Unreal's executable replaces the
  global `operator new`, and dyld binds the JIT's import to Unreal's allocator, so the JIT frees Unreal-allocated
  memory with `free()`. The .NET 10 JIT imports both operators and is unaffected, and `libcoreclr` imports both as
  well. The Mac flavor exists to exercise the host on the desktop; it is not intended for shipping.
