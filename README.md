# UnrealSharp

UnrealSharp is a free, open-source plugin for writing Unreal Engine 5 games in C# on top of .NET 10.

[Workflow Showcase](https://www.youtube.com/watch?v=xR7M2XgCuNU)

## Features

- **Unreal Engine API in C#**: Derive from any `UClass`. Implement Actors, ActorComponents, and more in C# with full access to the Unreal Engine API.
- **Generated bindings**: The C# API is automatically generated from all reflected C++ code. This includes the engine, plugins, and your own project, so any new reflected types or members are immediately available for use in C#.
- **Hot reload**: Recompile and reload C# code without restarting the editor.
- **Full .NET ecosystem**: Pull in any NuGet package you need.
- **MIT licensed**

## Games made with UnrealSharp

- [Ballistic Brews (Steam Page)](https://store.steampowered.com/app/4095000/Ballistic_Brews/)
- [Forged: Reconquest (Steam Page)](https://store.steampowered.com/app/3509080/Forged_Reconquest/)
- [Prompt Crisis (Steam Page)](https://store.steampowered.com/app/5203720/Prompt_Crisis/)

Making a game with UnrealSharp? [Open an issue](https://github.com/UnrealSharp/UnrealSharp/issues) or submit a PR to add your project here!

## Sample projects

- [Sample Defense Game](https://github.com/UnrealSharp/UnrealSharp-SampleDefenseGame) built for Mini Jam 174.
- [Slime Guzzler](https://github.com/UnrealSharp/Epic-MegaJam-Project) Epic MegaJam 2025 entry.
- [UnrealSharp-Cropout](https://github.com/UnrealSharp/UnrealSharp-Cropout) Epic's Cropout sample, ported from Blueprints to C#.

## Supported platforms

| Platform | Status |
|---|---|
| Windows | Supported |
| macOS | Supported |
| Linux | Planned |
| iOS | Planned |
| Android | Planned |

## Prerequisites

- Unreal Engine 5.6 – 5.8
- .NET 10.0.5 or newer
- A C++ project (strongly recommended, pure Blueprint projects work, but are harder to provide support for)

## Getting started

Visit the website's [Get Started](https://www.unrealsharp.com/getting-started/quickstart) page!

If you want to contribute to the documentation, check out the [docs repository](https://github.com/UnrealSharp/unrealsharp.github.io)!

## Source checkouts and packaged plugins

The plugin descriptor's `Installed` field distinguishes a source checkout (`false`)
from an official `BuildPlugin` distribution (`true`). This is separate from whether
the Unreal Engine itself is an Installed Build and where the plugin is located.

BuildPlugin distributes precompiled native modules, Automation commands, the UHT
exporter and Roslyn tools. It also includes the managed runtime source and matching
Editor/Game binding source, with a content identity for each target. Runtime DLLs
are built on first use rather than shipped. The .NET 10 SDK is required, including
access to the NuGet dependencies on the first restore.

For installed plugins, UHT and native Editor initialization ensure that the managed
Editor runtime is available under `~/.unrealsharp/Runtime/<identity>/Editor/Development`.
This runtime-only build consumes the packaged Roslyn DLLs and keeps all build outputs
out of the installed plugin directory. A completion manifest validates cached files;
concurrent projects using the same runtime wait on a build lock. Later launches reuse
the cache. Source changes in a new distribution produce a different identity.
Cook commandlets consume this prepared Editor cache without starting another UAT
inside BuildCookRun. If the cache has been removed, open the Editor or run
BuildPluginRuntime before cooking again.

PackageProject publishes a self-contained Game runtime into the same user cache,
separated by configuration and runtime identifier, then compiles project bindings
and user C# against those Game assemblies. The finished game carries its managed
runtime and does not require a .NET SDK. Source plugins retain their normal managed
build and publish paths.

With a source engine, source plugins can live in the project's `Plugins` directory
or the engine's `Engine/Plugins` directory. Automation commands use the normal
project or engine UAT build records, which track source dependencies. UBT also tracks
the exporter's project reference to the automation project. Build native changes
through the project's normal Unreal build entry point.

Install a complete packaged plugin into the matching Installed Engine's
`Engine/Plugins/Marketplace/UnrealSharp` directory. This path consumes the packaged
automation module, UHT exporter and Roslyn tools without recompiling them. User C#,
project bindings and the runtime-only solution still compile as described above.
Missing build tools, binding identities or incompatible engine bindings require a
complete matching package; they are not repaired by rebuilding the installed tools.
The package records which engine modules its runtime contains. Additional engine
modules enabled by a consuming project generate bindings under that project's
Intermediate directory and compile with its project glue assemblies.

BuildPlugin marks the output descriptor as installed after compilation. Source
builds therefore prepare the precompiled automation record for packaging, but do
not use that dependency-free record for source development. Installed plugins
validate the supplied record without rewriting it. `Installed` alone does not
make a binary package compatible with an arbitrary source-engine build.

## Code example

A networked, interactable resource pickup written entirely in C#:

```csharp
using UnrealSharp;
using UnrealSharp.Attributes;
using UnrealSharp.Engine;
using UnrealSharp.Niagara;

namespace ManagedSharpProject;

public delegate void OnIsPickedUp(bool bIsPickedUp);

[UClass]
public partial class AResourceBase : AActor, IInteractable
{
    public AResourceBase()
    {
        Replicates = true;
        RespawnTime = 500.0f;
    }

    [UProperty(DefaultComponent = true, RootComponent = true)]
    public partial UStaticMeshComponent Mesh { get; set; }

    [UProperty(DefaultComponent = true)]
    public partial UHealthComponent HealthComponent { get; set; }

    [UProperty(PropertyFlags.EditDefaultsOnly)]
    public partial int PickUpAmount { get; set; }

    [UProperty(PropertyFlags.EditDefaultsOnly | PropertyFlags.BlueprintReadOnly)]
    protected partial float RespawnTime { get; set; }

    [UProperty(PropertyFlags.BlueprintReadOnly, ReplicatedUsing = nameof(OnRep_IsPickedUp))]
    protected partial bool bIsPickedUp { get; set; }

    [UProperty(PropertyFlags.EditDefaultsOnly)]
    public partial TSoftObjectPtr<UNiagaraSystem>? PickUpEffect { get; set; }

    [UProperty(PropertyFlags.BlueprintAssignable)]
    public partial TMulticastDelegate<OnIsPickedUp> OnIsPickedUp { get; set; }

    public override void BeginPlay()
    {
        HealthComponent.OnDeath += OnDeath;
        base.BeginPlay();
    }

    [UFunction]
    protected virtual void OnDeath(APlayer player) {}

    public void OnInteract(APlayer player)
    {
        GatherResource(player);
    }

    [UFunction(FunctionFlags.BlueprintCallable)]
    protected void GatherResource(APlayer player)
    {
        if (bIsPickedUp)
        {
            return;
        }

        if (!player.Inventory.AddItem(this, PickUpAmount))
        {
            return;
        }

        UExperienceComponent experienceComponent = UExperienceComponent.Get(player.PlayerState);
        experienceComponent.AddExperience(PickUpAmount);

        SystemLibrary.SetTimer(OnRespawned, RespawnTime, false);

        bIsPickedUp = true;
        OnRep_IsPickedUp();
    }

    [UFunction]
    public void OnRespawned()
    {
        bIsPickedUp = false;
        OnRep_IsPickedUp();
    }

    [UFunction]
    public void OnRep_IsPickedUp()
    {
        if (PickUpEffect is not null)
        {
            UNiagaraFunctionLibrary.SpawnSystemAtLocation(this, PickUpEffect, GetActorLocation(), GetActorRotation());
        }

        OnIsPickedUpChanged(bIsPickedUp);
        OnIsPickedUp.Invoke(bIsPickedUp);
    }

    // Overridable from Blueprints
    [UFunction(FunctionFlags.BlueprintEvent)]
    public partial void OnIsPickedUpChanged(bool bIsPickedUp);
    public partial void OnIsPickedUpChanged_Implementation(bool bIsPickedUp)
    {
        SetActorHiddenInGame(bIsPickedUp);
    }
}
