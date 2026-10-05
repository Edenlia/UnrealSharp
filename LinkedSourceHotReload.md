# Linked C# sources in the Editor

The default project-directory watcher misses `Compile` items linked from outside
the `.csproj` directory. After Roslyn evaluates a project, `SolutionManager`
passes its actual document paths to the native editor subsystem. Registration
runs on the game thread. Additional recursive directory watchers accept only
those evaluated `.cs` paths; unrelated files and generated `obj`, `bin`, and
`Intermediate` outputs do not enter the compile queue. Watcher handles are
unregistered when the subsystem deinitializes. Resuming a paused reload consumes
the queued file changes once.

This supports edits to existing external Compile items. Newly introduced linked
files or changes to project membership require the solution to be evaluated
again (for example, reopening the Editor). The ordinary project-directory
watcher remains responsible for files created beneath the project itself.

Balloon verification uses its canonical Mac Development AutoBuild, followed by
the official Release BuildUserSolution task. In a normal Editor, the native
status reports 435 external source files across 95 watched directories. Saving
the actual floating component changes its reflected sea-level result from 0 to
77 and back; saving the shared height-modifier stack changes the computed height
from 0 to 100 and back. All four official OnScriptSave reloads succeed. An invalid
`.cs` file under an excluded nested directory causes no compilation. Exact
original source bytes are restored before running Opening in the same Editor.
Detailed process, build, and gameplay evidence lives in Balloon's
`Tools/ScriptComparison/OpeningPerformance` diagnostics, separate from Release
performance samples.
