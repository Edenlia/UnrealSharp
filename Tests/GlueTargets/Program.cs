using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using UnrealSharp.Shared;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

string fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N")));
GlueTarget Make(string project, string name = "ExampleEditor", string type = "Editor", string configuration = "Development", string architecture = "x64", string platform = "Win64")
{
    GlueTarget target = new()
    {
        ProjectDirectory = Path.Combine(fixture, project), PluginDirectory = Path.Combine(fixture, "Engine", "Plugins", "UnrealSharp"),
        ProjectFile = Path.Combine(fixture, project, project + ".uproject"),
        Name = name, Type = type, Platform = platform, Configuration = configuration, Architecture = architecture
    };
    target.IntermediateDirectory = Path.Combine(target.ProjectDirectory, "Intermediate", "Build", platform, architecture, name, configuration);
    return target;
}

GlueTarget original = Make("One");
GlueTarget[] distinct = { original, Make("Two"), Make("One", "OtherEditor"), Make("One", "Example", type: "Game"),
    Make("One", configuration: "Shipping"), Make("One", architecture: "arm64"), Make("One", platform: "Linux") };
Check(distinct.Select(target => target.Root).Distinct().Count() == distinct.Length, "Native targets must have distinct Intermediate directories.");
Check(original.Root == Path.GetFullPath(Path.Combine(original.ProjectDirectory, "Intermediate/Build/Win64/x64/ExampleEditor/Development")), "Use the target Intermediate directly, without a glue root or hash.");
Check(distinct.All(target => target.BindingsRoot == original.BindingsRoot), "Projects share the original framework output directory.");
Check(original.OutputDirectory == Path.Combine(original.ProjectDirectory, "Binaries", "Managed", "net10.0"), "Project outputs use the original flat path.");
Check(original.EngineGlue == Path.Combine(original.PluginDirectory, "Intermediate", "UnrealSharp", "UHT", "Editor"), "Fixed source retains Editor/Game separation.");
Check(original.OutputDirectory != distinct[1].OutputDirectory, "Different projects still have separate glue outputs.");
Check(original.OutputDirectory == distinct[3].OutputDirectory && original.Root != distinct[3].Root, "Editor/Game share the active project output but keep distinct generated source.");
original.Save();
Check(GlueTarget.Select(original.ProjectDirectory, "Editor", "Win64", "Development").Name == original.Name, "Project selection.");
distinct[2].Save();
Check(GlueTarget.Select(original.ProjectDirectory, "Editor", "Win64", "Development").Name == "OtherEditor", "Project source represents the most recently generated Target, as before.");
original.Save();
GlueTarget shipping = GlueTarget.Select(original.ProjectDirectory, "Editor", "Win64", "Shipping");
Check(shipping.Configuration == "Shipping" && shipping.Root == original.Root, "Packaging changes managed configuration without losing native generated sources.");
Check(GlueTarget.Read(original.ManifestPath).Root == original.Root, "Manifest round trip.");
GlueTarget universal = Make("Universal", architecture: "arm64+x64", platform: "Mac");
Check(universal.MatchesArchitecture("arm64") && universal.MatchesArchitecture("x64") && !universal.MatchesArchitecture("arm"),
    "Universal targets accept their exact constituent architectures.");
GlueTarget projectless = Make("EngineOnly", "UnrealEditor");
projectless.ProjectFile = "";
projectless.ProjectDirectory = Path.Combine(fixture, "Engine");
projectless.IntermediateDirectory = Path.Combine(projectless.ProjectDirectory, "Intermediate/Build/Win64/x64/UnrealEditor/Development");
Check(projectless.EngineGlue == Path.Combine(projectless.Root, "Engine") && projectless.OutputRoot == original.BindingsRoot,
    "A projectless bBuildAllModules context generates under Engine Intermediate and publishes to the flat plugin directory.");
projectless.Save();
string firstRoot = projectless.Root;
projectless.Name = "OtherEditor";
projectless.IntermediateDirectory = Path.Combine(projectless.ProjectDirectory, "Intermediate/Build/Win64/x64/OtherEditor/Development");
Check(firstRoot != projectless.Root, "Projectless native Targets must keep separate generation directories.");
projectless.Save();
string projectlessCatalog = Path.Combine(projectless.ProjectDirectory, "Intermediate", "Build");
bool rejected = false;
try { GlueTarget.SelectFrom(projectlessCatalog, "Editor", "Win64", "Development"); }
catch (InvalidOperationException) { rejected = true; }
Check(rejected, "Ambiguous projectless contexts require explicit selection.");
Check(GlueTarget.SelectFrom(projectlessCatalog, "Editor", "Win64", "Development", "OtherEditor")?.Root == projectless.Root, "Projectless name selection.");
string legacy = Path.Combine(projectlessCatalog, "legacy", GlueTarget.FileName);
GlueTarget.WriteIfChanged(legacy, "{\"Version\":2}");
Check(GlueTarget.SelectFrom(projectlessCatalog, "Editor", "Win64", "Development", "OtherEditor")?.Root == projectless.Root,
    "Obsolete manifests must be ignored without deleting caches.");
Check(GlueTarget.SelectFrom(projectlessCatalog, "Editor", "Win64", "Development", "OtherEditor", distributionOnly: true) == null,
    "A source engine build is not a distributable plugin bundle.");
Console.WriteLine("PASS: target Intermediate, flat project/projectless outputs, shared framework, Editor/Game source separation, configuration switching.");

GlueTarget package = Make("HostProject", "UnrealEditor");
package.Distribution = true;
package.Projects.Add(Path.Combine(package.Root, "Projects", "UnrealSharpMergedGlue", "UnrealSharpMergedGlue.csproj"));
string catalog = Path.Combine(package.PluginDirectory, "Binaries", "Managed", "BuildPlugin");
Check(package.BindingsRoot == Path.GetFullPath(Path.Combine(catalog, "Win64/x64/UnrealEditor/Development")), "BuildPlugin uses readable variant directories without Target IDs.");
GlueTarget.WriteIfChanged(Path.Combine(package.BindingsRoot, GlueTarget.FileName), JsonSerializer.Serialize(package));
GlueTarget.WriteIfChanged(Path.Combine(original.BindingsRoot, GlueTarget.FileName), JsonSerializer.Serialize(original));
GlueTarget crossPlatform = Make("ForeignHost", "UnrealEditor", platform: "Mac");
crossPlatform.Distribution = true;
crossPlatform.IntermediateDirectory = OperatingSystem.IsWindows() ? "/foreign/HostProject/Intermediate" : "C:\\ForeignHost\\Intermediate";
GlueTarget.WriteIfChanged(Path.Combine(crossPlatform.BindingsRoot, GlueTarget.FileName), JsonSerializer.Serialize(crossPlatform));
Check(GlueTarget.SelectFrom(catalog, "Editor", "Win64", "Development", distributionOnly: true)?.BindingsRoot == package.BindingsRoot,
    "A plugin package must not select a developer project's cached target.");
Check(GlueTarget.SelectFrom(catalog, "Game", "Win64", "Shipping", distributionOnly: true) == null,
    "Editor outputs must not satisfy a Game/Shipping request.");
string relocatedPlugin = Path.Combine(fixture, "Installed", "UnrealSharp");
package.MountDistribution(relocatedPlugin, Path.Combine(fixture, "Consumer", "Consumer.uproject"));
Check(package.Precompiled && package.PluginDirectory == relocatedPlugin && package.EngineGlue == Path.Combine(package.BindingsRoot, "Source"),
    "Relocation must preserve the bundled framework identity and replace build-machine paths.");
Check(package.Projects.Single() == "UnrealSharpMergedGlue.csproj", "HostProject source paths must not survive mounting.");
GlueTarget.WriteIfChanged(Path.Combine(package.BundleDirectory, "UnrealSharpMergedGlue.dll"), "test fixture");
GlueTarget.WriteIfChanged(Path.Combine(package.BundleDirectory, "GlueCode.LoadOrder.json"), "{\"LoadOrder\":[\"UnrealSharpMergedGlue\"]}");
package.CopyBundle(package.OutputDirectory);
Check(File.Exists(Path.Combine(package.OutputDirectory, "UnrealSharpMergedGlue.dll")), "Consuming project receives bundled glue.");
Check(package.OutputDirectory != package.BindingsDirectory, "Mounting must not publish into the shared plugin framework directory.");
Check(!package.IsDistributionBuild && package.OutputDirectory == Path.Combine(package.ProjectDirectory, "Binaries", "Managed", "net10.0"),
    "A distribution consumer uses flat project outputs while keeping the immutable framework bundle.");
package.Save();
Check(GlueTarget.SelectProject(package.ProjectDirectory, "Editor", "Win64", "Shipping") == null,
    "Precompiled Development bindings must not satisfy a Shipping request.");
Console.WriteLine("PASS: distribution selection, package relocation, per-project bundle deployment.");

if (args.Length == 3 && args[0] == "--distribution")
{
    DistributionChecks.Run(args[1], args[2], fixture);
    return;
}

foreach (string manifest in args)
{
    GlueTarget target = GlueTarget.Read(manifest);
    HashSet<string> optionalAssemblies = target.Projects.Select(path => Path.GetFileNameWithoutExtension(path)!).ToHashSet();
    Check(target.ModuleAssemblies["CoreUObject"] == "UnrealSharp", "Engine Source must be fixed.");
    Check(target.ModuleAssemblies["Engine"] == "UnrealSharp", "Engine Source must be fixed.");
    Check(target.ModuleAssemblies["EnhancedInput"] == "UnrealSharp", "Required EnhancedInput glue must be fixed.");
    foreach (string name in new[] { "StateTreeModule", "PluginBrowser" })
        if (target.ModuleAssemblies.TryGetValue(name, out string? assembly)) Check(assembly == "UnrealSharp", $"Required {name} must be fixed.");
    Check(target.ModuleAssemblies.Values.Contains(GlueTarget.MergedAssemblyName), "Expected optional engine plugin glue.");
    XDocument fixedInputs = XDocument.Load(Path.Combine(target.EngineGlue, "UnrealSharp.EngineGlue.props"));
    string inputs = fixedInputs.ToString();
    foreach ((string module, string assembly) in target.ModuleAssemblies)
        if (assembly == GlueTarget.MergedAssemblyName) Check(!inputs.Contains(">" + module + "/") && !inputs.Contains(")" + module + "/"), $"Optional module {module} leaked into fixed compile items.");
    string[] frameworkAssemblies = new[] { target.BindingsDirectory, Path.Combine(target.BindingsRoot, "netstandard2.0") }
        .SelectMany(folder => Directory.GetFiles(folder, "UnrealSharp*.dll", SearchOption.TopDirectoryOnly))
        .Where(path => !optionalAssemblies.Contains(Path.GetFileNameWithoutExtension(path))).ToArray();
    Check(frameworkAssemblies.Length >= (target.Type == "Editor" ? 13 : 12), "Framework build outputs are incomplete.");
    foreach (string path in frameworkAssemblies)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        Check(!metadata.AssemblyReferences.Any(handle => optionalAssemblies.Contains(metadata.GetString(metadata.GetAssemblyReference(handle).Name))), $"{Path.GetFileName(path)} must not depend on optional glue.");
    }
    using JsonDocument loadOrder = JsonDocument.Parse(File.ReadAllText(Path.Combine(target.OutputDirectory, "GlueCode.LoadOrder.json")));
    string[] assemblies = loadOrder.RootElement.GetProperty("LoadOrder").EnumerateArray().Select(item => item.GetString()!).ToArray();
    Check(assemblies.Contains(GlueTarget.MergedAssemblyName), "Merged glue must be in the load manifest.");
    Check(assemblies.ToHashSet().SetEquals(target.Projects.Select(path => Path.GetFileNameWithoutExtension(path)!)), "Load list must equal the current target's exact generated project set.");
    HashSet<string> fixedTypes = ReadPublicTypes(Path.Combine(target.BindingsDirectory, "UnrealSharp.dll"));
    foreach (string assembly in assemblies)
    {
        HashSet<string> types = ReadPublicTypes(Path.Combine(target.OutputDirectory, assembly + ".dll"));
        Check(!types.Overlaps(fixedTypes), $"{assembly} shadows a fixed type (a partial declaration may have crossed the assembly boundary).");
    }
    Console.WriteLine($"PASS: {target.Name} / {target.Platform} / {target.Configuration}: fixed membership, framework dependencies, exact glue load order.");
}

static HashSet<string> ReadPublicTypes(string path)
{
    using FileStream stream = File.OpenRead(path);
    using PEReader pe = new(stream);
    MetadataReader metadata = pe.GetMetadataReader();
    return metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
        .Where(type => (type.Attributes & System.Reflection.TypeAttributes.VisibilityMask) == System.Reflection.TypeAttributes.Public)
        .Select(type => metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name)).ToHashSet();
}
