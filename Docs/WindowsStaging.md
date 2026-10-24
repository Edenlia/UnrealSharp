# Windows managed staging

UnrealSharp can publish C# code during Unreal Engine's BuildCookRun staging step.
The managed assemblies and self-contained .NET runtime are registered as NonUFS
files before UE copies, packages and archives the build. A separate PackageProject
action is not needed for a build that used this staging hook.

## UE 5.7 and later

The hook is automatic when UAT loads UnrealSharp.Automation and stages a Windows
Game, Client or Server target whose receipt contains UnrealSharp. Engine plugins
are discovered by UAT. For a project plugin, building the native project also
generates `Build/UnrealSharp/UnrealSharpProject.Automation.csproj`, allowing source
engine UAT to discover the handler through the project's Build directory.

Use the normal project build and BuildCookRun workflow. CI must select the project
with `-ScriptsForProject=<project.uproject>` when necessary for UAT script discovery.
An explicit `-ScriptDir=<UnrealSharp>/Build/Scripts` also loads the plugin's commands
and handler. It does not cause a second managed publish when the generated project
entry is present.

Cook commandlets launched by UAT reuse its already-built AutomationTool and script
modules when building C# for asset loading. They use UAT's child-instance protocol
and separate log directories so they neither replace loaded tool assemblies nor
contend with the parent packaging process for its instance lock or logs. Editor commands use
normal project-plugin discovery in installed engines and a scoped plugin script
path where source/external plugin discovery requires it.

The generated project entry must remain available to UAT, including CI builds that
skip native compilation. It is generated from the plugin location used by UBT and
does not overwrite a user-authored automation project. If the plugin is removed,
the entry compiles as an empty automation module.

## UE 5.6

UE 5.6 has no automatic CustomStagingHandler hook. Load UnrealSharp.Automation
using `-ScriptDir=<UnrealSharp>/Build/Scripts` and add
`-customdeployment=UnrealSharp` to BuildCookRun.

Alternatively, projects can opt in through `DefaultEngine.ini`:

```ini
[/Script/WindowsTargetPlatform.WindowsTargetSettings]
CustomDeployment=UnrealSharp
```

The configuration option takes precedence over the command-line option. UE supports
only one custom deployment handler; do not replace another store or project handler
with this setting. Projects with another handler can continue to use the existing
manual PackageProject workflow. The UE 5.6 handler is excluded when the newer hook
is available, preventing duplicate publishing. Remove the UE 5.6
`CustomDeployment=UnrealSharp` setting and command-line option when upgrading to
UE 5.7 or later; those versions use the automatic staging hook instead.

## Output and boundaries

- Target type, platform, configuration and architecture come from the native build
  receipt, including targets whose names differ from the project name.
- Publish output is isolated under
  `Intermediate/UnrealSharp/Stage/<Platform>/<Target>/<Configuration>/<Architecture>/Binaries/Managed/<TFM>`.
  Each publish cleans its prior output using the existing PackageProject logic.
- UAT stages that output under `<Project>/Binaries/Managed/<TFM>` as NonUFS. This
  preserves the existing runtime loader's expected layout, including the installed
  flag, load-order manifests and runtime dependencies.
- One runtime target/configuration and one architecture are allowed per staging
  directory. Stage other targets or configurations into separate directories.
- Editor and Program targets, DLC, `-skipstage`, and platforms other than Win64 do
  not trigger automatic publishing. `-skipstage` reuses an already staged build;
  it does not refresh managed code.
- Native AOT remains unsupported. This change does not add Mac or mobile staging.
- A publish failure fails staging. Native builds and generated Game/Client/Server
  bindings must be current; an Editor build does not regenerate Game bindings.

The standalone PackageProject command and Editor Package Project action remain
available for existing pipelines and platforms outside this hook's scope.
