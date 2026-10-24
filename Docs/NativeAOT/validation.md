# NativeAOT change and verification journal

## Plan checkpoint — 9781aedd

Created the phased implementation plan after inspecting upstream/nativeaot, current main, the open trimming draft, Microsoft NativeAOT documentation, both test projects and their existing AutoBuild authority. No runtime support is claimed by this documentation commit.

Toolchain: macOS arm64, .NET SDK 10.0.401 / runtime 10.0.12; source UE reports 5.8.3. The explicit user scope includes the fork and both test projects. No additional repository-local guidance exists there.

## Upstream merge — 4899400f

Parents: current eden-main (plan checkpoint) and upstream/nativeaot `b19374cee4cf25a6a98fc3e0fda4ab063b80cfdd`.

Conflict resolution decisions:

- Keep current UTF-8 initialization/result ABI, hostfxr path conversion and error reporting. The prototype's old loader/export ABI is deferred to the separately tested bootstrap/host steps; this merge alone is not runnable NativeAOT support.
- Keep current `Invoke_*` ABI and the current ref/out fixes. Upstream changes UHT wrappers without changing the managed dispatcher or Roslyn wrappers; importing only that half would break JIT callbacks. Evaluate and implement the complete call path in the function step.
- Adapt delayed type/module registration to current `FieldName`, current JSON serialization and current `RegisterManagedType` signature.
- Preserve current target-aware packaging and project/DLL-reference support; add upstream's aggregate AOT publishing code.
- Retain current Build.props, which already defines `NATIVE_AOT` for `BuildingAOT=true`, and current conditional imports. Keep packaging opt-in instead of forcing NativeAOT through the Editor button.
- Preserve plugin duplicate-load guard while adding the upstream precompiled-assembly branch.

Preservation / reproducibility:

- Original test source snapshots and SHA-256 manifests: `<UE_ROOT>/Tests/NativeAOT/eden-main/original/A` and `B` (857 / 852 files). No Unreal binary asset contents were read or modified.
- A source checkout is fixed at `cb8577e3`; B is deployed from the reviewed merge worktree. Deployment manifests record source HEAD, patch hash and every copied file's hash.
- Six existing parameter regression files from A are preserved in `Tests/NativeAOT/Fixtures` and applied to B. These cover native/managed calls, Unicode strings, POD/non-POD structs, bitfields, object identity/nulls, ref/out and mixed return values.
- `Tests/NativeAOT/run.mjs` records exact commands, output and exits under `<UE_ROOT>/Tests/NativeAOT/eden-main/runs`. It invokes the existing AutoBuild entries; it does not modify their options or configuration. It checks process occupancy and gates Editor probes on a successful canonical build with matching native source hashes.

The merge was committed externally while validation was running. Its 816 deployed plugin files exactly match the pre-commit B deployment hashes. The commit has not been rewritten.

Initial evidence (all under the evidence runs directory):

| Run | Result | Interpretation |
| --- | --- | --- |
| `merge-jit-a` | Canonical Editor build exit 0, 99.54 s | Baseline native / managed glue compilation passed |
| `merge-jit-b` | Canonical Editor build exit 0, 93.99 s | Merge native / managed glue compilation passed |
| `merge-jit-probe-a` | Exit 1; 17 checks pass, then empty-string/null combined check fails | Pre-existing runtime behavior needs diagnosis; suite did not pass |
| `merge-jit-probe-b` | Exit 2; managed fixture absent | B had no UserCode manifest; not a valid runtime comparison yet |

Generated project inspection also found that both slots' glue imported the integrated Engine plugin instead of the intended project-local source. Do not treat the initial runtime probes as a completed merge regression. Fix source selection and use official generation/build commands to prepare current glue and user manifests before repeating the comparison. No generated glue is manually edited.

## Project plugin selection prerequisite

`GetUnrealSharpUPlugin` enumerated plugins and returned the first matching filename. With both the integrated and project copies present, it selected the Engine copy. Reuse the existing `ReadAvailablePlugins` helper, which applies UnrealBuildTool's version/override selection, and retrieve the selected UnrealSharp plugin by name.

Focused validation: existing `GenerateProject` UAT command in B exits 0 (`precedence-generate-b`) and now writes `../../../../../Plugins/UnrealSharp/UnrealSharp.Shared.props`. The previously generated file referenced `../../../../../../../UnrealEngine/Engine/Plugins/UnrealSharp/UnrealSharp.Shared.props`. No native build options, engine selection or project definition changed.

Negative control: the same official GenerateProject path in unchanged A exits 0 (`precedence-generate-a`) but imports the Engine plugin. This directly reproduces the source-selection error and distinguishes the corrected B result. The existing UBT filtered-plugin helper continues to support engine-only installations; that fallback is source-inspected, not separately exercised in another project.

### Source-identity correction and reproducible A/B driver

The initial direct-UAT evidence above is insufficient on its own: `controlled-glue-a` later failed inside the integrated Engine plugin's `GlueTargetUtilities`, a class absent from the deployed fork. UAT selects the first assembly/command with a given name. Preserve that failure; do not delete caches to make the test select another assembly.

The test driver now links the slot's unmodified production Automation sources into a uniquely named assembly and calls commands through its unique entry. `NATIVEAOT-AUTOMATION` and `NATIVEAOT-PLUGIN` record both identities. This is managed UAT compilation, not an alternate native build. It changes no production source logic. Ordinary Editor/menu dispatch with two installed plugin copies remains a separate unvalidated boundary.

`pinned-selection-b` confirms the fixed selector with explicit assembly identity. The `pinned-fixture`, `pinned-probe`, `pinned-solution`, `pinned-glue` and `pinned-user` rounds all exit 0 for both A and B, with project-local plugin identities and imports. A is the fixed `cb8577e3` baseline plus only the common `8e1b3a7f` source-selection prerequisite; B includes the upstream merge and that fix.

`pinned-runtime-a` / `pinned-runtime-b` both exit 1 after the same 18 passing checks. Their diagnostics show `StoredActor == null` is true, but `LastMessage` remains `input: 中文` after assigning an empty string. `StringMarshaller.FromNative` checks only the pointer, ignoring `ArrayNum`; an empty FString may retain allocated storage containing old bytes. This is a baseline bug, not a difference introduced by the NativeAOT merge. The remaining string checks have not yet run.

## FString length prerequisite

`StringMarshaller.FromNative` now returns empty when `ArrayNum <= 1` and otherwise copies exactly `ArrayNum - 1` UTF-16 code units, excluding the terminator. This respects FString's length even when its storage remains allocated. The fixture counts only PASS records, excluding diagnostic lines.

Focused negative controls are the two `pinned-runtime` failures above. B's `string-build-b` managed production solution build exits 0 (22 existing analyzer/trimming warnings, no errors); `string-user-b` exits 0; `string-runtime-b` exits 0 with **22 PASS**. The previously failing nonempty-to-empty assignment now passes, as do Unicode, 8192-character output, null UObject, ref/out and mixed return checks. No native inputs changed, so the successful canonical Editor build still gates this runtime probe. This establishes a passing merged JIT path for the fixture; it does not yet establish NativeAOT support.
