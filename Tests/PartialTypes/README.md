# Partial type reflection and reload regression

Run `dotnet run --project Tests/PartialTypes/PartialTypes.Tests.csproj` with the
plugin's .NET SDK. This console test links the production Editor change detector
and references the real glue generator. It does not load Unreal or deploy plugin
binaries. It verifies additions, removals, constructor changes, unchanged saves,
namespace collisions, attribute aliases, and repeated generator-driver updates
that leave the annotated declaration's syntax tree untouched.

For Editor verification, start with a reflected partial Actor in one file and an
unannotated partial declaration in a second file. Create a Blueprint child, keep
the Editor open, and add an editable partial UProperty only to the second file.
After the official source-save reload, check the inherited property in the child.
Rename/change its type, remove it, and repeat. Also add/remove a separate partial
file while the attributed declaration remains unchanged. Property and callable
reflection must follow each change without touching the UClass file. Retain the
Editor log; the console checks alone do not establish Blueprint reinstancing.
