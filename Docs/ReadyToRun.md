# Experimental mobile ReadyToRun

Experimental iOS arm64 support, built on macOS. NativeAOT remains the default.
Dripping has passed its canonical Development build, signing, installation,
CoreCLR/R2R startup, managed type registration and entry-map loading on an
iPhone 14 Pro running iOS 18.7.8. The user also confirmed the Level 1 touch
controls and Level 4 tunnel/Radio/re-entry smoke tests on this R2R package.
Android integration and device validation are not implemented by this change.

## Runtime model and prerequisites

ReadyToRun retains managed IL and metadata alongside precompiled code. It is not
NativeAOT, and is not a standalone native library exporting the managed methods.
On iOS, CoreCLR uses its interpreter for methods without usable R2R code; it must
not rely on permission to JIT. Reflection and IL loading have different limits
from NativeAOT. This initial integration deliberately uses process-lifetime
assemblies, not collectible plugin contexts or runtime hot reload.

The machine's .NET 10.0.401 / runtime 10.0.12 is sufficient for the existing
UnrealSharp compilation and NativeAOT paths. iOS CoreCLR with Mach-O composite
R2R requires .NET 11. The publisher and device build used SDK
`11.0.100-rc.3.26522.148` and its matching runtime/crossgen2 packs. No global SDK
upgrade or MAUI workload is required for this generic `net11.0` aggregate.

Use the project's canonical iOS AutoBuild entry with these process environment
variables, without changing native build options:

- `UNREALSHARP_IOS_RUNTIME=ReadyToRun`
- `UNREALSHARP_R2R_DOTNET=<absolute path to isolated .NET 11 dotnet executable>`

Unset the runtime variable (or use `NativeAOT`) for the existing default.
The standalone UnrealSharp `PackageProject` command also accepts `-ReadyToRun`;
it cannot be combined with `-NativeAOT`. The SDK selection is for the aggregate
publisher only; it does not change the UE tools or Editor's .NET runtime.

UE's archive copy can retain obsolete files in an existing `.app`. When switching
runtime modes, preserve the old generated archive elsewhere and use an empty
archive destination. Verify the resulting archive with strict deep code-signature
validation before installation. The staged, signed app is the source of truth;
copying new files over a previous runtime's archive can invalidate its seal.

## Packaging and hosting

1. Publish the actual target's UnrealSharp, glue and user assemblies as IL,
   without `NATIVE_AOT` compilation. Keep Editor outputs separate.
2. Reference those binaries in a `net11.0` aggregate. Enable self-contained
   `ios-arm64`, `PublishReadyToRun`, `PublishReadyToRunComposite`, and the `macho`
   container format. Do not trim reflection-dependent code.
3. Let the .NET SDK invoke crossgen2 and its native-link step. Wrap the resulting
   R2R image and runtime dylibs in frameworks, updating install names and local
   dependencies before signing. Remove the runtime pack's original signatures so
   Xcode does not preserve a signing identifier that mismatches the new framework
   bundle identifier. Keep managed component assemblies loose and
   preserve their filename case with SystemNonUFS inside the normal cookeddata
   directory. Convert UE's virtual assembly directory to its physical iOS path
   before appending the original CLR filename.
4. Declare all produced frameworks through the plugin's existing Xcode framework
   integration. UE embeds and signs them; no UE source patch is required.
5. The iOS host loads CoreCLR and the signed composite, supplies its `RTR_HEADER`
   through `host_runtime_contract.get_native_code_data`, exposes runtime properties
   through `get_runtime_property` (including the physical CoreLib directory), and
   initializes the existing UnrealSharp callback contract through
   `coreclr_create_delegate`. CoreCLR errors are forwarded to the UE log.
6. Resolve native BCL P/Invokes to the loaded frameworks. Use the default assembly
   context and the host's target assembly list instead of desktop hostpolicy's
   `AssemblyDependencyResolver`. Reject unload before changing registry state.
7. Generate native callback wrappers that explicitly marshal UTF-8 string inputs,
   convert delegate inputs to function pointers while keeping them alive, and pin
   unmanaged `ref/out` parameters. The public C# wrapper signatures stay the same;
   the underlying unmanaged calls use pointers. This avoids runtime-generated
   `calli` marshalling stubs, which require a JIT on these paths.

The SDK/runtime, component assemblies and composite image must be published
together. A marker or successful `dlopen` alone is not evidence of R2R execution.
The host logs when CoreCLR requests the composite's native-code data; device
validation must also exercise callbacks, reflection, delegates, generics and
gameplay. Performance versus NativeAOT has not been measured. This initial build
is untrimmed; Dripping's generated app is approximately 1.3 GB. Simulator,
Shipping/App Store distribution, third-party native package integration and
hot reload are outside the validated scope.

## Validation on 2026-10-30

- The actual publisher passes initial/repeated publication and rejects a .NET 10
  SDK. A probe with UAT's inherited MSBuild overrides confirms the selected
  isolated .NET 11 SDK is used.
- Canonical Dripping AutoBuild includes Mac Editor prerequisites, IOS arm64
  native compilation, Cook, managed target publication, full optimized composite
  R2R, Xcode signing and archive. The installed archive passes strict deep
  signature validation; framework signing identifiers match their bundle IDs.
- On device, CoreCLR requests the signed composite, initializes UnrealSharp,
  loads the target assemblies, registers managed types and loads `L_Entry`.
- Tests of the actual generated wrappers pass with warnings treated as errors:
  null/empty/ASCII/Unicode/long strings, multiple strings, delegate callbacks
  across forced GC, null delegates, pinned heap struct fields, and `ref/out`
  writes. Mac Cook also exercises the changed wrappers on the desktop runtime.
- Before/after crossgen2 diagnostics confirm the property-string, set-delegate
  and FName-byref wrappers no longer require `GetCookieForPInvokeCalliSig` runtime
  JIT. These diagnostics complement the canonical build and device execution.
- The investigation also found missing manual layouts for `FVector3f`,
  `FVector4f` and `FMatrix44f`. Their actual target assembly layouts now match
  native sizes 12/16/64 and field offsets; the full optimized R2R compiler passes
  without excluding methods or disabling optimization.
- User-confirmed Level 1 device smoke test passed: opening sequence, movement,
  stopping on release, touch look, and window movement/Jump/Drop.
- User-confirmed Level 4 device smoke test passed: at least two natural tunnel
  loops, Radio interaction, music starting after the exit, no replay on repeated
  interaction, and restoration of the initial loop after leaving and re-entering.

Local reproduction projects and build/device logs are retained under Dripping's
ignored `Saved/ReadyToRun-20261030` directory, not included in implementation
commits.

## Sources inspected on 2026-10-30

- [Microsoft: CoreCLR on mobile in .NET 11](https://devblogs.microsoft.com/dotnet/dotnet-maui-moves-to-coreclr-in-dotnet-11/)
- [CoreCLR interpreter tracking](https://github.com/dotnet/runtime/issues/112748)
- [iOS SDK CoreCLR bring-up](https://github.com/dotnet/runtime/issues/120051)
- [Platform-native R2R design](https://github.com/dotnet/runtime/blob/2b2365405ba9d5c4a448ca07629d1a771dcc8b35/docs/design/coreclr/botr/readytorun-platform-native-envelope.md)
- [Host/runtime contract](https://github.com/dotnet/runtime/blob/2b2365405ba9d5c4a448ca07629d1a771dcc8b35/src/native/corehost/host_runtime_contract.h)
- [Apple SDK R2R framework implementation](https://github.com/dotnet/macios/blob/410dc42e09f1bfca3258d46d5ec95cf48903d1b2/dotnet/targets/Microsoft.Sdk.R2R.targets)
- [Tested RC3 CoreLib lookup](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/binder/assemblybindercommon.cpp)
- [.NET native interop guidance](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices)

The vendored host contract header retains its .NET Foundation MIT notice. Its
source is the pinned runtime commit above. Public mobile application support is
not a guarantee for this custom embedding host; that boundary requires testing.
