# Existing Blueprint enum mappings

`[BlueprintEnum("/Game/Folder/E_State.E_State")]` maps a plain C# byte enum to
the existing UserDefinedEnum. It does not register a C# UEnum. Include every
real entry in asset order with the exact numeric value; omit Unreal's generated
MAX entry. C# member labels are independent of localized Blueprint display names.
Do not also use UEnumAttribute.

The generator emits the asset path and ordered values on enum properties and
function parameters. It omits a managed assembly dependency for the mapped enum.
Native property creation loads that exact asset and validates its identity,
entry count and values before reusing the UEnum. Existing enum marshalling stays
byte based. Invalid mappings fail instead of silently creating a replacement.
The referenced asset must be available in the runtime/cooked content as usual.

A plugin C# edit needs the official BuildSolution command on Managed/UnrealSharp,
even when UHT reports no changed engine glue. Then run BuildUserSolution.
Native changes still require the target project's canonical AutoBuild first.

Balloon verification: Tools/ScriptComparison/SharpFoundation/foundation-13.json
in the host project. AutoBuild74, plugin managed01 and user managed30 succeeded.
Editor13_f433c1f2 verified original enum identity in three properties and two
function signatures, all values, mismatched enum rejection, and a Blueprint
child override called from C# with the mapped enum parameter. No authored assets
were saved. This is not cooked or live enum-asset editing verification.
