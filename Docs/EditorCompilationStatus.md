# Editor compilation status

`CSHotReloadSubsystem.GetCompilationStatus` provides a coherent game-thread
snapshot of workspace readiness, paused/pending state and compile counters.
The counters surround the existing synchronous `PerformHotReload` operation;
completion includes assembly reload and dependent Blueprint refresh. A native
log error or invalid resulting object must still fail the caller's verification.

The exposed `PerformHotReload` UFUNCTION is the same action as the editor menu.
It retains the existing pending-change, PIE/SIE and pause checks. No native
compile path, forced compile or new scheduling mechanism is introduced.

Balloon AutoBuild99 passed with its unchanged target/options. Three runtime
status fields and counters were observed in normal Editor PIE algorithm runs;
the initial completed/failed counters were zero and remained unchanged while
timing. Hot-reload behavior itself is verified separately by live C# and BP
return values during the memory workflow.
