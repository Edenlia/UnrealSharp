# Source navigation integration checks

Managed parent controls are installed by `FCSBlueprintEditorCustomization` through
public Kismet, ToolMenus and toolkit command APIs. No `BlueprintEditor.cpp` patch is
required. Menu regeneration and parent changes schedule the managed overlay for the
next core ticker, after Kismet has installed its own overlay. Changing to a content
Blueprint or native parent restores Kismet's normal controls.

The plugin creates a C# source hyperlink and edit icon, with no Content Browser
button. It also hides and disables My Blueprint's Move Variable/Function to Parent
commands for immediate managed parents. The original actions remain in use for
ordinary content parents, including after reparenting, and are restored when the
customization is removed. Stock Kismet's `IsParentClassABlueprint()` still recognizes
managed Blueprint shells; command safety does not depend on changing that predicate.
Other scripting plugins retain their normal engine behavior.

`UnrealSharp.SourceNavigation` inspects real Blueprint editor widgets and can invoke
their existing Slate click delegates without desktop input or Unreal Automation Tests.
Build the project's normal Editor target before starting verification. Use the
project's configured engine and launch an explicit `.uproject` with the runtime
`-cmdlink` option if using the engine's `CmdLink.exe` client.

```text
CmdLink.exe UnrealSharp.SourceNavigation /Game/Path/BP_Example.BP_Example open
CmdLink.exe UnrealSharp.SourceNavigation /Game/Path/BP_Example.BP_Example inspect
CmdLink.exe UnrealSharp.SourceNavigation /Game/Path/BP_Example.BP_Example parent
```

Run `open` first and allow a Slate tick before inspection or another action.
Reopen/focus the target asset after an IDE launch, since its editor widgets may no
longer be visible. Do not repeat a command after an unknown execution timeout;
inspect the Editor log first. A connection failure before sending a command can be
retried after the CmdLink pipe reconnects.

Actions:

| Action | Operation |
| --- | --- |
| `open` | Open or focus the supplied content Blueprint. |
| `inspect` | Report visible editor widget tags, visibility, enabled state and arranged X coordinates. |
| `parent` | Click the parent source-name hyperlink if exactly one visible match exists. |
| `edit` | Click the parent edit icon if exactly one visible match exists. |
| `component` | Click each visible `Edit in C#` hyperlink; use a full Blueprint editor with a component tree. |
| `browse` | Attempt to locate a tagged parent browse control; managed overlays must report zero matches and no source requests. |
| `asset` | Activate the parent class's generated asset through `UAssetEditorSubsystem`, exercising the C# asset action. |

The command temporarily wraps the selected source accessor in memory and restores
it on exit; it does not persist accessor preferences. By default, managed source
requests are captured without launching an IDE. Append `launch` to forward them to
the real selected IDE, for example:

```text
CmdLink.exe UnrealSharp.SourceNavigation /Game/Path/BP_Example.BP_Example component launch
```

The `SOURCE_NAVIGATION_REPORT` JSON contains the resolved file, line and column,
matching button count, widget positions and number of open assets before/after.
An empty match is not a successful click. For C# parents, require absent browse
controls, a source label before the edit icon, and a request pointing at the actual
class declaration. Stable parent tags are supplied by the plugin's C# overlay;
stock native/content parent widgets are not tagged by an Engine patch. Normal
content parent commands are exercised separately by `UnrealSharp.ParentRegression`.
Native parents retain the standard engine source hyperlink, without the additional
native edit icon that the retired Engine patch supplied.

`accepted` means the accessor accepted the request, not that the IDE opened the
file. Confirm real launches separately using the IDE's received-command and
file-open logs, plus its active file. Keep capture-only results distinct from
actual IDE verification. Native C++ navigation can dispatch asynchronously and is
outside this synchronous managed-request capture check.

If Rider reports `DirectoryLock`/Unix socket errors while its original instance
still responds, preserve that instance and inspect launcher context before
removing locks or restarting the IDE. Test from a normally launched project Editor
to distinguish inherited execution-environment restrictions from source dispatch.
The verification command does not change system/IDE security settings or save assets.

To cover the shared parent predicate's My Blueprint callers, run:

```text
CmdLink.exe UnrealSharp.ParentRegression /Game/Path/BP_ManagedLeaf.BP_ManagedLeaf
```

Supply an actor Blueprint whose immediate parent is a managed class. This opt-in
Editor command creates four uniquely named, unsaved fixtures. It selects local
variables/functions in the real My Blueprint widget and checks the registered
command visibility for content Blueprint, native and managed parents. Managed
parent commands must additionally reject execution through `FUIAction`. For the
content parent it executes the production Move to Parent delegates, verifies the
member transfer and recompiles both Blueprints. Native and managed actions remain
hidden and are never executed. Only fixture editors are closed and only fixture
package dirty flags are cleared; original assets are never modified or saved.
Require `PARENT_REGRESSION_COMPLETE failures=0 fixtures=4 saved=0`, rather than
treating command dispatch as success. The fixtures remain in memory until normal
Editor shutdown, so run in a disposable verification session before packaging.
