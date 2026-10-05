# Blueprint-owned scene roots

`[UnrealSharp.Core.Attributes.UMetaData("NoDefaultSceneRoot")]` opts an Actor
class out of the synthetic `DefaultSceneRoot_UnrealSharp` fallback. Native,
inherited and explicitly declared scene components are still resolved or
promoted normally. This is useful for a component-free C# behavior layer placed
between a native Actor type and an existing Blueprint whose SCS owns the root.

The option is read from class reflection data in both runtime compilation and
the Editor skeleton compiler. It does not depend on editor-only UClass metadata
being applied before construction. Existing classes retain the default behavior
unless they explicitly opt out.

The first skeleton now receives its intended superclass before the function
factory enumerates inherited event signatures. Without it, initial skeleton
construction can miss C# overrides even though the generated class contains
them, causing BP parent-call node reconstruction to resolve a native ancestor.

Validation is recorded in Balloon's `SharpCutover` snapshots and Foundation
fixtures. A native build by itself is not an asset-preservation or runtime proof.
Balloon AutoBuild97 and official managed build90 succeeded. Foundation52 verifies
43 groups, including direct C# native Tick dispatch and two BP layers whose
enabled event/parent chains each accumulate 0.75 in C#. Cutover07_1ec36148 passed
all 104 class signatures and 200 in-memory BP contract comparisons with zero
unreviewed differences, managed/BP errors or ensures. These are correctness
results; they do not establish hotcompile, full gameplay or performance parity.

`[UnrealSharp.Core.Attributes.UMetaData("NoAutoTick")]` preserves the native
parent's tick flags instead of automatically enabling tick when C# overrides
`Tick`. The event remains callable. The option uses the nearest managed class's
reflection data during CDO/skeleton compilation and instance refresh. Classes
without it retain the existing automatic behavior. Balloon uses it only on
OpeningSceneEnd and GameEndTrigger: the original Puerts Editor export shows
`bCanEverTick=false` on both assets, despite their TS ReceiveTick methods.
