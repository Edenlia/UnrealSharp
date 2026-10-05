# Readonly managed function parameters

The function generator handled C# `out`, `ref` and ordinary value parameters,
but omitted the `in` case. The generated metadata therefore lacked `CPF_Parm`
on an `in` argument, and the registered UFunction silently lost that parameter.

`in` now emits `Parm | OutParm | ReferenceParm | ConstParm`, matching a native
const-reference input. The generated invocation reads it but does not copy it
back after the call. A parameter-only `[Const]` attribute retains `ConstParm`
for native value inputs without changing their C# passing convention. Writable
`ref`/`out` arguments reject `[Const]` rather than producing contradictory flags.

Official managed plugin06 and user82 builds passed. Balloon's normal Editor
Foundation45 verified a vector `in` plus const float through ProcessEvent (24
from `(1,2,3)` and 4), including actual native flags. That run later failed its
separate ref-output fixture; it is retained as a failed overall run. The complete
SharpCutover02_ad245c48 audit passed for 104 BP roots/200 saved BPs, with zero
managed-function signature differences after adapter name/ref fixes. No native
assets were saved by the audit. Full cutover and runtime verification remain
separate evidence.
