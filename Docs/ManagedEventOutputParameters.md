# Managed event output parameters

The source generator now treats C# `out` as output-only and copies `out` and
`ref` values back after calling the reflected Unreal function. The previous
wrapper read unassigned C# outputs and never copied results back, preventing
Balloon's detachable-mesh event from compiling with its original output pins.
Native-to-managed dispatch also avoids reading an output-only value.

Managed call wrappers initialize their native parameter buffer before writing
values and destroy it in `finally`, after copying results into managed storage.
This supports owned FString and container values in addition to scalars and
object pointers. Destruction visits the same first NumParms properties as the
existing initializer; Blueprint local properties outside the caller's buffer
are excluded. Function containers use the existing copy marshallers.

Verified through Balloon's canonical AutoBuild75, official plugin BuildSolution02
and BuildUserSolution33. Editor Foundation15_72fdfe57 passed 18 checks with no
managed or Blueprint errors: sixteen roundtrips of ref integer, out bool/object/
Unicode string/integer array plus a string return; a disposable BP component
overrode the real detachable-mesh event and returned its own bool/object outputs.
The complete prior reflection/Construction/Blueprint dispatch checks also pass.
Foundation14's fixture retained an automatic parent call; its exception is kept
as a failed run. The corrected fixture connects entry directly to result.
