# Reflection and Editor reload across partial type declarations

The glue generator previously inspected only members in the syntax declaration
carrying `UClass`/`UStruct`. A `UFunction` in another partial file silently lost
reflection; a Blueprint event or partial property also failed to compile because
its generated implementation was missing.

`InspectionDispatcher` now visits every declaring syntax reference and resolves
members using that syntax tree's semantic model. Member syntax remains available
for property accessor/specifier handling. Partial method definitions and
implementations are deduplicated by their canonical definition symbol. The
existing primary-constructor path already inspects the complete symbol table.

The Editor also previously decided which native types needed rebuilding by
looking for a UClass/UStruct attribute on the changed declaration's syntax.
Changing an unannotated partial file could therefore generate and reload a new
assembly without marking its Unreal type dirty. Native reflection and Blueprint
children retained the old properties until the attributed declaration changed.

`UnrealTypeChangeDetector` resolves the changed declarations in the old and new
incremental compilation snapshots. The merged type symbol supplies the Unreal
attribute even when it is written in another file. Structural changes now dirty
that type through the existing native reload path. File additions, removals and
constructor changes are included; unchanged saves and trivia-only edits remain
ignored. Fully qualified type identities keep unrelated same-named types apart.

The standalone regression checks in `Tests/PartialTypes` exercise the production
detector and the real incremental glue generator. All 32 checks passed, including
repeated edits that retain the attributed syntax tree unchanged.

The Editor reload regression was reproduced in Balloon on Mac ARM64 / UE 5.8.3
with the generation-only fix installed: source-save compilation and reload
succeeded, but the added property was absent from the managed parent and its
Blueprint child and grandchild. With the complete fix, eight consecutive saves
or file deletions passed: create an empty part, add a property, rename/change its
type, remove it, add a new partial file, delete that file, add the property again,
and delete the remaining extra file. Reflected property presence/absence and
read/write behavior were checked on all three classes after each reload. The
UClass file remained byte-identical and no Blueprint was manually recompiled
after reload. Temporary sources were removed and no Blueprint assets were saved.
Official plugin BuildSolution 07 and user BuildUserSolution 235 both exited 0;
the canonical AutoBuild native invocation also passed before Editor verification.
The fixture, raw logs and manifests are retained in Balloon under
`Saved/ScriptComparison/R1PartialReload`. This is a Balloon integration test,
not a claim of independent-project coverage for every reflected member kind.

Earlier generation validation used official plugin BuildSolution 05 and user
BuildUserSolution 81 (both exit 0). Normal Editor Foundation44_36993dd0 exits 0:
an unannotated partial file's UProperty roundtrips 12 → 19 through its UFunction;
the production player-binding callable is present in native reflection; the
microbenchmark host's Blueprint event dispatches its C# default and a BP override
1000 times each. No duplicate UClass attributes are needed. The fixture and raw
logs live in Balloon's Tools/ScriptComparison/SharpFoundation and corresponding
Docs/ScriptComparison/Evidence/SharpFoundation directory.
