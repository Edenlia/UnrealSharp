# Empty sparse multicast delegates

Balloon's tutorial migration exposed a native crash when managed code broadcast
`AActor.OnActorBeginOverlap` after removing its final listener. Unreal returns a
null multicast storage pointer for an unbound sparse delegate. The former binder
resolved that pointer and unconditionally dereferenced it in `BroadcastDelegate`
and `ContainsDelegate`. `IsBound` instead interpreted the sparse wrapper itself as
a regular multicast delegate.

All queries now resolve storage through the reflected property. Empty storage
means an unbound delegate, no matching listener, an empty string and a no-op
broadcast. Inline multicast delegates continue to use their original address.
The native and managed callback signatures for `IsBound` and `ToString` change
together; rebuild both the native plugin and official managed solution before
loading the plugin.

Reproduction and validation live in Balloon's
`Tools/ScriptComparison/SharpFoundation/ABalloonSharpInteropProbe.cs`:
query and broadcast an empty actor overlap delegate, bind a reflected C# callback,
query and broadcast it, remove the last listener, then query and broadcast again.
The same fixture also exercises regular controller delegates and retains their
argument/filter/removal assertions. Foundation18 records the original SIGSEGV.
This is a functional regression check, not a performance sample.

Foundation19_299302e9 passed with native AutoBuild78, official plugin managed
BuildSolution03 and user BuildUserSolution39. The full run exited zero with
20 check groups and no managed or Blueprint errors.
