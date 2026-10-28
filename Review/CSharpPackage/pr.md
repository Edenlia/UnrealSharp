# Publish managed code during staging and align Editor packaging

UnrealSharp currently requires a separate managed publish after packaging the Unreal project, as described in the [packaging documentation](https://www.unrealsharp.com/getting-started-and-fundamentals/packaging). This change publishes the assemblies and self-contained runtime while UAT builds the staging manifest, so platform packaging and signing can include them in the same pipeline.

The plugin reuses PackageProject, derives the target, configuration and architecture from the native receipt, publishes into an isolated Intermediate directory, and registers the output as NonUFS. Publishing must use the plugin recorded in that receipt; a publish failure stops the pipeline before archiving. Projects do not need changes to their Target files, descriptor or packaging configuration, or an additional Automation bridge.

## Editor and command-line entry points

| Engine capability | Editor | Command line |
| --- | --- | --- |
| CustomStagingHandler available, tested on UE 5.7/5.8 | Use Unreal's normal Platforms > Package Project. The redundant UnrealSharp Package Project button is removed. | Standard BuildCookRun with the project's script context |
| No CustomStagingHandler, tested on UE 5.6 | UnrealSharp > Package Project now runs the complete host-platform packaging pipeline and accepts an empty output directory. | BuildCookRunUnrealSharp |

The legacy command derives from BuildCookRun and attaches the shared staging implementation through ModifyDeploymentContextCallback. UE 5.6's native Platforms action remains unchanged; use the UnrealSharp action or the dedicated command there. Both entry points are selected by API availability, rather than a hard-coded engine minor version.

The legacy Editor action saves dirty packages and reads the selected host-platform target, configuration, architecture and common packaging options. It uses the existing asynchronous UAT helper. Turnkey's full command builder is private, so this action does not reproduce every Turnkey option.

PreBuild/PostBuild do not reliably cover managed-only changes when native actions are skipped. Command dispatch also resolves only the requested command type, preventing UHT from loading the legacy command's UAT-only dependencies through eager type enumeration.

## Validation

Validated project-level plugins on Installed UE 5.6.1, 5.7.4 and 5.8.3, Win64 x64 Development Game.

- Actual Editor menu A/B checks on all three engines. On 5.6, the old action rejected an empty archive; the new action packaged successfully. Canceling directory selection did not start UAT. On 5.7/5.8, the plugin action disappeared and native Editor packaging succeeded.
- All three candidate packages ran a managed probe from the new archive, loading the packaged runtime and expected managed content. Publish, Stage and Archive hashes matched for 220/220/225 files.
- Canonical native Editor/Game builds passed before opening Editor. UI packaging used Build=Never to keep native compilation separate from Editor DLL occupancy.
- Earlier original-base A/B checks cover managed-only changes, compiler failure without archive replacement, skipped staging/recovery, and 54 receipt/command boundaries. These checks remain applicable to the unchanged staging/publishing implementation.
- Earlier generated-output comparison matched 94,905 UHT text artifacts. The new Editor commit does not change exporters. Current published production assemblies and runtime dependencies match the preceding candidate byte for byte; only the test probe and its content changed.

Also verified UE 5.8.3 source build with an engine-level plugin, Win64 x64 Development Client and dedicated Server, using actual clicks on Unreal's Platforms > Package Project menu. The targets were named `CSharpPackageClient` and `CSharpPackageDedicated`, both different from the project name; the server target does not use a `Server` suffix.

- Standard BuildCookRun selected the correct target type and automatically published managed code during staging, with no separate managed packaging action or project Automation bridge.
- Both archived executables exited successfully after running a C# startup probe. The probes confirmed `WITH_CLIENT`/`WITH_SERVER`, `WITH_DEVELOPMENT`, expected content, and assemblies/runtime loaded from their respective archives. Each target's 220 managed files matched across Publish, Stage and Archive.
- Canonical Editor/Client/Server/UnrealPak builds passed before opening Editor; UI packaging used Build=Never. These are packaging and managed initialization checks, not multiplayer interoperability or rendering tests.

Previously measured warm Stage-plus-manual-publish versus automatic Stage medians were 9.904/10.086 s, 9.847/10.140 s and 42.160/42.523 s, respectively, with one warmup and three samples per side. These exclude native build, cook and runtime verification. The latest complete Editor runs are functional checks, not a new performance benchmark.

## Scope and limitations

The staging path supports Win64 and Mac JIT; Mac packaging/signing was not executed on the Windows test host. Mobile, NativeAOT and universal architectures are not added. Mixed targets/configurations and DLC are rejected. Shipping, Client/Server combinations beyond the UE 5.8.3 source-engine case above, and engines older than 5.6 remain untested.

The legacy Editor action targets the host platform and does not forward custom DDC graph or ZenStore options. IfProjectHasCode conservatively requests a native build instead of replicating Turnkey's promoted-receipt optimization. Source-engine project-plugin discovery may require an explicit ScriptDir.

Plugin discovery precedence is unchanged; duplicate engine/project installations remain a separate concern.

Addresses [#786](https://github.com/UnrealSharp/UnrealSharp/issues/786). If this staging workflow is accepted as the replacement for post-packaging publishing, [#785](https://github.com/UnrealSharp/UnrealSharp/pull/785) can be superseded: the Client/Server and custom target-name cases are verified here, and staging lets UAT place managed files before platform packaging/signing instead of locating and modifying an already packaged executable or Mac bundle. Mac execution/signing still needs platform validation. This is a workflow replacement, not a port of #785's additional options for the standalone legacy PackageProject command.
