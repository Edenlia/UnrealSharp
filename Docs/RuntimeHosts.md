# Runtime hosts

UnrealSharp brings managed code into a process through a *runtime host*: a `FCSRuntimeHost` subclass in
`Source/UnrealSharpCore/Private/Runtime/` that starts its runtime and resolves the managed `InitializeUnrealSharp`
entry point. Everything after that (calling the entry point, reporting managed exceptions,
`-waitformanageddebugger`) is shared in `FCSRuntimeHost::Initialize`.

| Kind | Host | Runtime | Platforms | Typical use |
|---|---|---|---|---|
| `CoreClrHostFxr` | `FCSHostFxrRuntimeHost` | CoreCLR started through hostfxr (shared or self-contained .NET) | Win64, Mac | Editor, desktop JIT packages |
| `NativeAot` | `FCSNativeAotRuntimeHost` | A NativeAOT shared library; there is no separate runtime | Win64, Mac, iOS, Android | Shipping builds without a JIT |

`CoreClrEmbedded` is reserved in `ECSRuntimeKind` and `RuntimeFlavor` for a host that starts CoreCLR through
`coreclr_initialize`; no platform packages it and no build compiles that host yet.

A .NET runtime can only be started once per process and cannot be restarted, so a host is initialized once and
lives until the process exits. Hot reload happens inside the runtime through collectible `AssemblyLoadContext`s.

## Capabilities

Code outside the hosts should ask what the active runtime can do instead of which runtime it is:

```cpp
const FCSRuntimeCapabilities Capabilities = FUnrealSharpCoreModule::Get().GetRuntimeCapabilities();
```

| Capability | CoreClrHostFxr | NativeAot |
|---|---|---|
| `bCanLoadAssembliesFromPath` | yes | no |
| `bSupportsCollectibleLoadContexts` | yes | no |
| `bSupportsHotReload` | Editor only | no |
| `bAssembliesArePrelinked` | no | yes |

`IsNativeAOT()` remains as a deprecated forwarder to `bAssembliesArePrelinked`.

## How the host is chosen

`RuntimeHostFactory::CreateRuntimeHost` decides:

1. The Editor always uses `CoreClrHostFxr`; it builds and hot reloads managed code.
2. A packaged build reads `UnrealSharp.Runtime.json` from its managed directory (`Binaries/Managed/net10.0`).
3. Without a manifest, an `UnrealSharp.NativeAOT` marker (written by older packaging) means `NativeAot`.
4. Without either, platforms that compile the hostfxr host fall back to it; other platforms fail with an error.

Which hosts exist in a build is decided at compile time by `UnrealSharpCore.Build.cs`:
`UNREALSHARP_WITH_HOSTFXR_HOST` (not iOS or Android) and `UNREALSHARP_WITH_NATIVEAOT_HOST` (all).

### The runtime manifest

Packaging writes the manifest; `FCSRuntimeManifest` reads it.

```json
{
  "Version": 1,
  "Kind": "NativeAot",
  "Library": "libMyGame.framework/libMyGame"
}
```

Library references are platform relative (`PlatformUtilities::ResolveBundledLibraryPath`): relative to the managed
directory on desktop, to the app's `Frameworks` directory on iOS, and a bare file name on Android.

## Choosing the runtime when packaging

The packaging scripts resolve a `RuntimeFlavor` (same names as the runtime kinds), in this order:

1. `-Runtime=<Kind>` on `PackageProject` (`-NativeAOT` is an alias for `-Runtime=NativeAot`);
2. the `"Runtime"` setting in `Config/UnrealSharp.Settings.json`, either a string or an object keyed by platform
   with an optional `"Default"`;
3. the legacy `"NativeAOT": true` setting;
4. the platform default: `CoreClrHostFxr` on Win64 and Mac, `NativeAot` on iOS and Android.

```json
{
  "Runtime": { "Default": "CoreClrHostFxr", "IOS": "NativeAot" }
}
```

Each platform lists the runtimes it can package (`PlatformUtilities.SupportedRuntimes`); asking for another fails
before anything is built.
