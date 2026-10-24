# NativeAOT incremental validation

The runner uses the existing `SharpTestProject` (A) and `SharpTestProject1` (B) under the workspace's `UEProjects` directory with **Installed UE 5.8** at `/Users/Shared/Epic Games/UE_5.8`. It invokes their existing `AutoBuild_Mac.sh` / `AutoBuild.json` for Editor and `AutoBuild_Game_Mac.sh` / `AutoBuild_Game.json` for Game. Read those files before running a new target. Do not change an entry to make a test pass.

Evidence is written to `UnrealEngine/Tests/NativeAOT/eden-main`; every round name must be unique. `original/A` and `original/B` preserve the pre-task source. A's plugin source comes from the fixed `baseline` Git worktree; B comes from this fork. Deployment does not copy or read Unreal assets and does not replace Binaries/Intermediate.

Deployment removes obsolete files only when a previous deployment owned them. Pre-existing local compilation inputs are preserved and recorded with hashes in `retained`; inspect these when comparing slots. In particular, both slots retain their original GlueGenerator.Tests/OutParameters.cs required by the existing Game AutoBuild provenance manifest.

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

`uat A|B <round> <command> <arguments...>` uses Installed UE's ordinary UAT dispatcher and the project-local production Automation assembly. Installed UE contains no Engine-level UnrealSharp. The previous renamed/source-linked dispatcher has been removed.

The initial fixture is the pre-existing parameter regression from A, preserved under `Fixtures` with matching relative paths. Before the first native build, copy those six files to B after preserving B. Existing native module and project definitions already include these files. The original snapshots allow restoration without guessing which files predated this work.

After deploying B, `node Tests/NativeAOT/bootstrap.mjs <unique-round>` generates the production AOT bootstrap, references the real runtime projects, publishes an osx-arm64 shared library into isolated artifacts, and calls it through `NativeLibrary` and unmanaged function pointers. A small test-only UAT command references the ordinary production Automation project to access its export generator; it neither renames nor recompiles production sources into another assembly. Test-only exports inspect the working directory and exercise the supplied bind callback. The caller verifies the export, ABI layout, callback tables, Unicode and failure reporting; it never unloads the NativeAOT library. This focused check does not validate UE startup, trimming of game types, or the aggregate packaging command. Review the preserved publish warnings separately.

Append `dispatch` to also exercise the actual `ManagedCallbacks` table with explicitly rooted mechanism fixtures: type lookup, constructor/native-pointer ordering, inherited/virtual calls, input/ref/out/return buffers, static bodies, GC, Wrap, struct initialization, delegate invocation and disposal. These fixtures intentionally isolate the core calling convention from UHT/Roslyn metadata generation; full UE checks are still required.

The shared fixture also supports standalone Game. Copy all `Fixtures` text files to each slot, preserving the original slot first. After both Editor and Game canonical builds succeed, `archive B <round>` cooks/stages/archives with native compilation disabled. Publish with normal `uat ... PackageProject`, using `<evidence>/archives/<round>/Mac/<project>.app/Contents/UE/<project>` as ArchiveDirectory and Game/Development/Mac/arm64 target parameters. Append `-NativeAOT` for B's AOT package. `game B <run-round> <archive-round>` runs the packaged fixture and records loaded dyld images.

Packaged macOS apps write reports inside their existing app-container Saved directory; the runner copies them into the evidence round. A passing exit code alone is insufficient: probe/game rounds require the report's TOTAL PASS marker and no FAIL/ERROR entries. The native binary, AutoBuild configuration and app sandbox/signing settings are unchanged by the runner.

`editor A|B <round>` starts the normal Installed Editor with a small Python controller and the C# file watcher enabled. Wait for both `python-ready.json` and `C# Hot Reload is ready.` before copying a managed fixture change into Script. After the successful build/reload message, write `request.json` in that round with `{"id":"after-reload","action":"run"}`. The controller creates two transient actors, calls the reflected managed test, writes the report/result and destroys the actors. Send a new ID with `action: "quit"` to close this task-owned Editor. The controller times out after 15 minutes; no assets are saved. Run slots sequentially.

When switching from Game generation back to Editor, first use the ordinary `BuildUserGlue -TargetType=Editor -TargetConfiguration=Development -OutputDirectory=<project>/Binaries/Managed/net10.0 -AddReferences=true` command if Editor module DLLs are absent. An Editor cannot validate reload when its initial glue set is incomplete.

For repeated managed-only steps, `node Tests/NativeAOT/live.mjs B <round> <expected-count> ChangedFixture.cs ParameterBufferManagedTests.cs` drives that sequence automatically. It waits for readiness, snapshots/copies only the named fixture C# files, waits for the real Hot Reload success, requires the exact report count, then closes its Editor. No-op changes are rejected. Run A and B sequentially, then publish the B Game payload and compare its PASS names with both JIT reports. A driver failure retains the complete evidence round.

`node Tests/NativeAOT/negative.mjs <unique-round> <B-archive-round>` exercises the actual packaged host with an invalid library, a valid NativeAOT library lacking the export, and one returning initialization failure. It backs up/restores only the task-owned AOT dylib and verifies its SHA-256. Small negative libraries are built with ordinary dotnet publish; Unreal native compilation still belongs exclusively to AutoBuild. Expected failures must include the precise error, no passing report and no JIT runtime images. NoLogThread/forcelogflush keep fatal evidence from being lost at process termination. Follow this with a normal game run to verify the restored payload.

Append `-TestIterations=3` to `probe` or `game` to run the full suite in three successive transient Worlds within one process. Each iteration releases argument storage, destroys its World, performs native GC and requires the World, actors and test UObject weak references to become invalid. Require three managed TOTAL markers, three native-GC PASS lines and `WORLDS 3 PASS`. The commandlet uses its explicit result as exit status because the exception-boundary fixture intentionally logs an error; it still rejects every error except that exact fixture diagnostic. Expected errors remain visible in the UE summary.
