# Glue solution cache regression checks

Run with a .NET 10 SDK:

```text
dotnet run --project Tests/GlueSolutionCache.Tests/GlueSolutionCache.Tests.csproj
```

The executable links the production `GlueSolutionCache.cs` directly and requires
no Unreal installation or external test packages. It creates a fresh temporary
artifact directory by default and prints the path. An optional first argument
selects an empty directory; a nonempty directory is rejected so stale DLLs cannot
hide a missing output.

Checks cover cache reuse, additions/removals, normalized path set semantics,
legacy poisoned stamps, missing/modified solutions, invalid cache JSON, and the
initial empty-project path. The final check uses real `dotnet new sln`, `sln
add/remove`, and `publish` commands to prove a newly added DLL is built, removed
projects stop participating, and stale solution membership recovers. Each publish
uses a separate output directory. The solution-level output warning is expected
in this isolated two-project fixture; the projects share no output file names.

This is a managed build test. It does not verify UHT generation, Editor startup,
plugin enablement, load-order cleanup, or C# hot reload. Those require the relevant
Unreal project workflow. The optional `CacheImplementation` MSBuild property is
used only for running the same checks against a fixed baseline implementation.
