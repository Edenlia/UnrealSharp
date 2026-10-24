# NativeAOT incremental validation

The runner uses the existing `SharpTestProject` (A) and `SharpTestProject1` (B) under the workspace's `UEProjects` directory. It requires their existing `AutoBuild_Source_<Target>_Mac.sh` and matching JSON files and the source UnrealEngine checkout. Read those files before running a new target. Do not change an entry to make a test pass.

Evidence is written to `UnrealEngine/Tests/NativeAOT/eden-main`; every round name must be unique. `original/A` and `original/B` preserve the pre-task source. A's plugin source comes from the fixed `baseline` Git worktree; B comes from this fork. Deployment does not copy or read Unreal assets and does not replace Binaries/Intermediate.

Run from this repository with Node:

```sh
node Tests/NativeAOT/run.mjs preserve A original-a
node Tests/NativeAOT/run.mjs preserve B original-b
node Tests/NativeAOT/run.mjs deploy A baseline-a
node Tests/NativeAOT/run.mjs deploy B candidate-b
node Tests/NativeAOT/run.mjs native A baseline-build Editor
node Tests/NativeAOT/run.mjs probe A baseline-probe
node Tests/NativeAOT/run.mjs native B candidate-build Editor
node Tests/NativeAOT/run.mjs probe B candidate-probe
```

These commands are sequential. Do not redeploy or edit a build's inputs while it is running. Do not launch a probe after a failed build. A successful probe must also contain the expected check count and success markers in `checks.txt`.

`uat A|B <round> <command> <arguments...>` runs supported managed build/generation commands through a uniquely named UAT assembly that links the selected slot's exact production sources. This avoids the Engine/project `UnrealSharp.Automation` name collision without substituting production logic. Logs include both assembly and selected plugin paths; source hashes are saved alongside the command. This does not establish that ordinary Editor-menu UAT dispatch selects the right copy when both installations coexist.

The initial fixture is the pre-existing parameter regression from A, preserved under `Fixtures` with matching relative paths. Before the first native build, copy those six files to B after preserving B. Existing native module and project definitions already include these files. The original snapshots allow restoration without guessing which files predated this work.

After deploying B, `node Tests/NativeAOT/bootstrap.mjs <unique-round>` generates the production AOT bootstrap through the identified UAT driver, references the real runtime projects, publishes an osx-arm64 shared library into isolated artifacts, and calls it through `NativeLibrary` and unmanaged function pointers. Test-only exports inspect the working directory and exercise the supplied bind callback. The caller verifies the export, ABI layout, callback tables, Unicode and failure reporting; it never unloads the NativeAOT library. This focused check does not validate UE startup, trimming of game types, or the aggregate packaging command. Review the preserved publish warnings separately.
