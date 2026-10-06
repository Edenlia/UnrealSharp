# Tick constructor setting: candidate independent reproduction

Status: observed in Balloon ClassAudit26 (`26_e80d3bea`), not yet reproduced in
an independent clean project. This is evidence for reviewing the existing
`NoAutoTick` extension, not authorization or a proposal to add another plugin
workaround. Plugin HEAD remains `ad7d043bdd7f1a2974a2f9308aae562cb5021a6c`.

## Observed failure

After removing OpeningSceneEnd_CS's pre-existing `NoAutoTick` metadata, its C#
constructor sets PrimaryActorTick.CanEverTick=false. The registered actor still
reports true through the ordinary generated C# tick-property API. The original
BP contract reports false. Managed233 compiled successfully; ClassAudit26 failed
on this exact comparison after the new trigger and its independent BP leaf
passed component/default checks. Logs remain under
`Saved/ScriptComparison/SharpDirectPort/ClassAudit/26_e80d3bea`.

The source chain is `UCSManagedClassCompiler::FinalizeManagedCDO` →
`CreateManagedObjectFromNative` → `SetupDefaultTickSettings`. The last function
first copies the native parent's tick flags, then sets them from the presence of
ReceiveTick. This runs after the managed constructor. Additional compiler paths
also call SetupDefaultTickSettings. The custom NoAutoTick branch predates this
resumed turn; restoring the original metadata preserves the existing behavior
but leaves its removal unresolved.

## Minimal proposal for the second Mac ARM

Use a separate small project with the same engine revision and an unmodified
UnrealSharp version pinned for comparison. Record engine/plugin/.NET versions.
Compile native setup only with that project's canonical AutoBuild entry point;
compile managed code through the plugin's normal user-solution workflow. Do not
copy build commands from Balloon or apply our NoAutoTick patch to the control.

Place this in one C# file so R1's split-partial question is not involved:

```csharp
using UnrealSharp.Attributes;
using UnrealSharp.Engine;

[UClass]
public partial class ATickDisabled_CS : AActor
{
    public ATickDisabled_CS()
    {
        var tick = PrimaryActorTick;
        tick.CanEverTick = false;
        tick.StartWithTickEnabled = true;
        PrimaryActorTick = tick;
    }

    public override void Tick(float deltaSeconds) { }

    [UFunction(FunctionFlags.BlueprintCallable)]
    public bool ReadCanEverTick() => PrimaryActorTick.CanEverTick;
}
```

After the class is registered, call ReadCanEverTick on its CDO. For the generated
class-path convention used by the pinned Balloon plugin, Editor Python is:

```python
cls = unreal.load_class(None, '/Script/UnrealSharp.TickDisabled_CS_C')
assert cls
print(unreal.get_default_object(cls).call_method('ReadCanEverTick'))
```

Expected if the explicit constructor default is honored: false. Suspected
result from the observed source chain: true. If registration uses another path,
use the actual generated class path shown by that version rather than assuming
the class failed to register.

Negative control: remove only the Tick override, rebuild normally, and repeat.
Also check a disposable BP leaf for each variant. Report CDO and placed-instance
results separately. A maintainer may consider automatic Tick enablement
intentional; ask what supported mechanism preserves an explicitly disabled
constructor default before labelling this a confirmed upstream bug.
