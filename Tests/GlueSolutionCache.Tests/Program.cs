using System.Diagnostics;
using System.Text.Json;
using UnrealSharp.Automation.Utilities;

// Links the production cache implementation. No Unreal binaries or NuGet packages are needed.
string Root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "UnrealSharpGlueCacheTests", Guid.NewGuid().ToString("N"));
if (Directory.Exists(Root) && Directory.EnumerateFileSystemEntries(Root).Any())
{
    Console.Error.WriteLine("Choose a fresh, empty artifact directory so stale outputs cannot hide a missing DLL.");
    return 2;
}
Directory.CreateDirectory(Root);
List<string> Failures = new();
int Passed = 0;

void Test(string name, Action<string> action)
{
    string TestDirectory = Path.Combine(Root, name);
    Directory.CreateDirectory(TestDirectory);
    try
    {
        action(TestDirectory);
        Console.WriteLine($"PASS {name}");
        Passed++;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"FAIL {name}: {exception.Message}");
        Failures.Add(name);
    }
}

void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

(string Solution, string Stamp, string[] Projects) Seed(string directory)
{
    string Solution = Path.Combine(directory, "UnrealSharpGlue.sln");
    File.WriteAllText(Solution, "test solution content");
    string[] Projects = [Path.Combine(directory, "A.csproj"), Path.Combine(directory, "B.csproj")];
    GlueSolutionCache.Save(Solution, Projects);
    return (Solution, Path.Combine(directory, "UnrealSharpSolutionStamp.json"), Projects);
}

Test("unchanged-set", directory =>
{
    var State = Seed(directory);
    Require(GlueSolutionCache.IsCurrent(State.Solution, State.Projects), "An unchanged solution must be reused.");
});

Test("added-project", directory =>
{
    var State = Seed(directory);
    Require(!GlueSolutionCache.IsCurrent(State.Solution, State.Projects.Append(Path.Combine(directory, "New.csproj"))), "New project must invalidate the solution.");
});

Test("removed-project", directory =>
{
    var State = Seed(directory);
    Require(!GlueSolutionCache.IsCurrent(State.Solution, State.Projects.Take(1)), "Removed project must invalidate the solution.");
});

Test("reordered-and-duplicate-paths", directory =>
{
    var State = Seed(directory);
    Require(GlueSolutionCache.IsCurrent(State.Solution, [State.Projects[1], State.Projects[0], State.Projects[0]]), "Enumeration order and duplicate paths must not force regeneration.");
});

Test("normalized-paths", directory =>
{
    var State = Seed(directory);
    Require(GlueSolutionCache.IsCurrent(State.Solution, ["./A.csproj", "child/../B.csproj"]), "Equivalent relative paths must match.");
});

Test("platform-case-semantics", directory =>
{
    var State = Seed(directory);
    bool Actual = GlueSolutionCache.IsCurrent(State.Solution, State.Projects.Select(path => path.ToUpperInvariant()));
    Require(Actual == OperatingSystem.IsWindows(), "Windows paths ignore case; other platforms conservatively distinguish case.");
});

Test("missing-solution", directory =>
{
    var State = Seed(directory);
    File.Delete(State.Solution);
    Require(!GlueSolutionCache.IsCurrent(State.Solution, State.Projects), "Missing solution must regenerate even if the stamp exists.");
});

Test("modified-solution", directory =>
{
    var State = Seed(directory);
    File.AppendAllText(State.Solution, "changed");
    Require(!GlueSolutionCache.IsCurrent(State.Solution, State.Projects), "An independently modified solution must invalidate its stamp.");
});

Test("legacy-poisoned-stamp", directory =>
{
    var State = Seed(directory);
    File.WriteAllText(State.Stamp, JsonSerializer.Serialize(State.Projects));
    Require(!GlueSolutionCache.IsCurrent(State.Solution, State.Projects), "Legacy arrays cannot prove solution membership and must regenerate once.");
});

Test("malformed-stamp", directory =>
{
    var State = Seed(directory);
    File.WriteAllText(State.Stamp, "{truncated");
    Require(!GlueSolutionCache.IsCurrent(State.Solution, State.Projects), "Broken cache JSON must become a miss, not terminate the build.");
});

Test("invalid-stamp-fields", directory =>
{
    var State = Seed(directory);
    File.WriteAllText(State.Stamp, "{\"Version\":2,\"Projects\":[null],\"SolutionHash\":\"invalid\"}");
    Require(!GlueSolutionCache.IsCurrent(State.Solution, State.Projects), "Invalid persisted paths must not be trusted.");
});

Test("empty-project-set", directory =>
{
    var State = Seed(directory);
    Require(!GlueSolutionCache.IsCurrent(State.Solution, []), "Removing the last project changes the input set.");
    GlueSolutionCache.Invalidate(State.Solution);
    Require(!File.Exists(State.Stamp), "The empty-project fast path must discard its stamp.");
    Require(!GlueSolutionCache.IsCurrent(State.Solution, State.Projects), "Returning from an empty set requires fresh generation.");
});

Test("initial-empty-set-without-directory", directory =>
{
    string Solution = Path.Combine(directory, "not-created", "UnrealSharpGlue.sln");
    GlueSolutionCache.Invalidate(Solution);
    Require(!Directory.Exists(Path.GetDirectoryName(Solution)), "An empty initial build must not create cache directories.");
    Require(!GlueSolutionCache.IsCurrent(Solution, []), "An absent cache cannot be current.");
});

Test("failed-generation-keeps-invalid-cache", directory =>
{
    var State = Seed(directory);
    File.WriteAllText(State.Solution, "partially generated after failure");
    Require(!GlueSolutionCache.IsCurrent(State.Solution, State.Projects), "Without a successful Save, partial generation must stay invalid.");
});

void DotNet(string workingDirectory, params string[] arguments)
{
    ProcessStartInfo StartInfo = new("dotnet") { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string Argument in arguments) StartInfo.ArgumentList.Add(Argument);
    using Process Process = System.Diagnostics.Process.Start(StartInfo)!;
    Task<string> StdOut = Process.StandardOutput.ReadToEndAsync();
    Task<string> StdErr = Process.StandardError.ReadToEndAsync();
    Process.WaitForExit();
    string Output = StdOut.GetAwaiter().GetResult() + StdErr.GetAwaiter().GetResult();
    File.AppendAllText(Path.Combine(workingDirectory, "dotnet.log"), $"dotnet {string.Join(' ', arguments)}\n{Output}\n");
    Require(Process.ExitCode == 0, $"dotnet exited {Process.ExitCode}; see {workingDirectory}/dotnet.log");
}

Test("real-solution-add-remove-and-recovery", directory =>
{
    string Solution = Path.Combine(directory, "UnrealSharpGlue.sln");
    string MakeProject(string name)
    {
        string ProjectDirectory = Path.Combine(directory, name);
        Directory.CreateDirectory(ProjectDirectory);
        File.WriteAllText(Path.Combine(ProjectDirectory, $"{name}.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(ProjectDirectory, "Probe.cs"), $"namespace {name}; public class Probe {{ }}");
        return Path.Combine(ProjectDirectory, $"{name}.csproj");
    }
    string First = MakeProject("First");
    string Added = MakeProject("Added");
    int GenerationCount = 0;
    void Build(string[] projects, string outputName)
    {
        if (!GlueSolutionCache.IsCurrent(Solution, projects))
        {
            DotNet(directory, "new", "sln", "--format", "sln", "--name", "UnrealSharpGlue", "--force");
            DotNet(directory, ["sln", Solution, "add", "--include-references", "false", "--in-root", .. projects]);
            GenerationCount++;
        }
        DotNet(directory, "publish", Solution, "--configuration", "Release", "--output", Path.Combine(directory, outputName), "--verbosity", "quiet");
        GlueSolutionCache.Save(Solution, projects);
    }
    Build([First], "initial");
    // Reproduce the previous bug's already-poisoned stamp: new list, old solution.
    File.WriteAllText(Path.Combine(directory, "UnrealSharpSolutionStamp.json"), JsonSerializer.Serialize(new[] { First, Added }));
    Build([First, Added], "added");
    Require(File.Exists(Path.Combine(directory, "added", "Added.dll")), "New glue DLL was not built after a project was added.");
    Build([Added, First], "unchanged");
    Require(GenerationCount == 2, "Reordering project discovery should reuse the solution.");
    DotNet(directory, "sln", Solution, "remove", Added);
    Build([First, Added], "membership-repaired");
    Require(File.Exists(Path.Combine(directory, "membership-repaired", "Added.dll")), "A removed solution entry must regenerate even if the versioned stamp still lists the project.");
    Build([First], "removed");
    Require(!File.Exists(Path.Combine(directory, "removed", "Added.dll")), "The removed project must not be built into a fresh output.");
    File.Delete(Solution);
    Build([First], "recovered");
    Require(File.Exists(Path.Combine(directory, "recovered", "First.dll")), "Missing solution was not recovered.");
    Require(GenerationCount == 5, "Unexpected regeneration count.");
});

Console.WriteLine($"RESULT passed={Passed} failed={Failures.Count} artifacts={Root}");
return Failures.Count == 0 ? 0 : 1;
