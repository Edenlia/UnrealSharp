using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;
using UnrealSharp.Shared;

internal static class DistributionChecks
{
    // Exercise the actual Automation helper and UE's FileFilter without invoking
    // UBT, UHT or BuildPlugin. All writes stay in this test's temporary fixture.
    public static void Run(string sourcePlugin, string engine, string fixture)
    {
        sourcePlugin = Path.GetFullPath(sourcePlugin);
        engine = Path.GetFullPath(engine);
        string automationPath = Path.Combine(sourcePlugin, "Build/Scripts/bin/Development/UnrealSharp.Automation.dll");
        if (!File.Exists(automationPath))
            automationPath = Path.Combine(sourcePlugin, "Build/Scripts/uatbin/Development/UnrealSharp.Automation.dll");
        string[] roots = { Path.GetDirectoryName(automationPath)!, Path.Combine(engine, "Binaries/DotNET/AutomationTool"), Path.Combine(engine, "Binaries/DotNET/UnrealBuildTool") };
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string root in roots)
            {
                string? path = Directory.EnumerateFiles(root, name.Name + ".dll", SearchOption.AllDirectories).FirstOrDefault();
                if (path != null) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        Assembly automation = AssemblyLoadContext.Default.LoadFromAssemblyPath(automationPath);
        Type targetType = automation.GetType("UnrealSharp.Shared.GlueTarget", throwOnError: true)!;
        MethodInfo publish = automation.GetType("UnrealSharp.Automation.Utilities.GlueTargetUtilities", throwOnError: true)!
            .GetMethod("PublishRuntimeContext")!;
        Type utilities = publish.DeclaringType!;
        GlueTarget flat = new()
        {
            Name = "ExampleEditor", Type = "Editor", Platform = "Win64", Configuration = "Development", Architecture = "x64",
            ProjectDirectory = Path.Combine(fixture, "ExplicitProject"), ProjectFile = Path.Combine(fixture, "ExplicitProject", "Example.uproject"),
            PluginDirectory = Path.Combine(fixture, "SharedPlugin")
        };
        flat.IntermediateDirectory = Path.Combine(flat.ProjectDirectory, "Intermediate/Build/Win64/x64/ExampleEditor/Development");
        flat.Projects.Add(Path.Combine(flat.Root, "Projects", "UnrealSharpMergedGlue.csproj"));
        object flatTarget = JsonSerializer.Deserialize(JsonSerializer.Serialize(flat), targetType)!;
        var arguments = (IEnumerable<string>)utilities.GetMethod("BuildArguments")!.Invoke(null, new[] { flatTarget, (object)false })!;
        if (arguments.Any(argument => argument.Contains("CustomBeforeDirectoryBuildProps")))
            throw new Exception("An explicit project must not redirect MSBuild obj/bin.");
        utilities.GetMethod("WriteBuildProps")!.Invoke(null, new[] { flatTarget });
        XDocument references = XDocument.Load(Path.Combine(flat.Root, "UnrealSharp.References.targets"));
        if (references.Descendants("HintPath").Single().Value != "$(UnrealSharpUserAssemblyDirectory)/UnrealSharpMergedGlue.dll")
            throw new Exception("Packaging must be able to redirect user references to its published runtime glue.");
        if (references.Descendants("Private").Single().Value != "true")
            throw new Exception("Glue must remain a publish input so SDK incremental cleanup does not remove it from shared project outputs.");
        Console.WriteLine("PASS: actual Automation keeps project MSBuild paths and permits archive-local glue references.");
        GlueTarget Create(string name, string type, string configuration = "Development")
        {
            GlueTarget target = new()
            {
                Name = name, Type = type, Platform = "Win64", Configuration = configuration, Architecture = "x64", Distribution = true,
                ProjectDirectory = Path.Combine(fixture, "Package/HostProject"),
                ProjectFile = Path.Combine(fixture, "Package/HostProject/HostProject.uproject"),
                PluginDirectory = Path.Combine(fixture, "Package/HostProject/Plugins/UnrealSharp")
            };
            target.IntermediateDirectory = Path.Combine(target.ProjectDirectory, "Intermediate", "Build", target.Platform, target.Architecture, name, configuration);
            target.Projects.Add(Path.Combine(target.Root, "Projects/UnrealSharpMergedGlue/UnrealSharpMergedGlue.csproj"));
            GlueTarget.WriteIfChanged(target.PropsPath, "<Project />");
            GlueTarget.WriteIfChanged(Path.Combine(target.EngineGlue, "CoreUObject/Object.generated.cs"), "// fixed glue fixture");
            GlueTarget.WriteIfChanged(Path.Combine(target.EngineGlue, "UnrealSharp.EngineGlue.props"), "<Project />");
            GlueTarget.WriteIfChanged(Path.Combine(target.OutputDirectory, "UnrealSharpMergedGlue.dll"), "fixture glue");
            GlueTarget.WriteIfChanged(Path.Combine(target.OutputDirectory, "GlueCode.LoadOrder.json"), "{\"LoadOrder\":[\"UnrealSharpMergedGlue\"]}");
            return target;
        }
        GlueTarget editor = Create("UnrealEditor", "Editor");
        GlueTarget game = Create("UnrealGame", "Game");
        GlueTarget shipping = Create("UnrealGame", "Game", "Shipping");
        string filterPath = Path.Combine(editor.PluginDirectory, "Config/FilterPlugin.ini");
        GlueTarget.WriteIfChanged(filterPath, File.ReadAllText(Path.Combine(sourcePlugin, "Config/FilterPlugin.ini")));
        GlueTarget foreign = Create("BalloonEditor", "Editor");
        foreign.ProjectDirectory = Path.Combine(fixture, "PreviousHostProject");
        GlueTarget.WriteIfChanged(Path.Combine(foreign.BindingsRoot, GlueTarget.FileName), JsonSerializer.Serialize(foreign));
        GlueTarget.WriteIfChanged(Path.Combine(editor.PluginDirectory, "Binaries/Managed/Targets/legacy", GlueTarget.FileName), "{\"Version\":1}");
        foreach (GlueTarget target in new[] { editor, game, shipping, editor })
            publish.Invoke(null, new[] { JsonSerializer.Deserialize(JsonSerializer.Serialize(target), targetType)! });

        Assembly core = Assembly.Load("EpicGames.Core");
        Type filterType = core.GetType("EpicGames.Core.FileFilter", true)!;
        object exclude = Enum.Parse(core.GetType("EpicGames.Core.FileFilterType", true)!, "Exclude");
        object filter = Activator.CreateInstance(filterType, exclude)!;
        object file = Activator.CreateInstance(core.GetType("EpicGames.Core.FileReference", true)!, filterPath)!;
        filterType.GetMethod("ReadRulesFromFile")!.Invoke(filter, new object[] { file, "FilterPlugin", Array.Empty<string>() });
        bool Included(string path) => (bool)filterType.GetMethod("Matches")!.Invoke(filter, new object[] { path })!;
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        foreach (GlueTarget target in new[] { editor, game, shipping })
        {
            string directory = Path.GetRelativePath(target.PluginDirectory, target.BindingsRoot).Replace('\\', '/');
            Check(Included($"{directory}/net10.0/UnrealSharp.dll"), "Framework omitted from package");
            Check(Included($"{directory}/Glue/net10.0/UnrealSharpMergedGlue.dll"), "Merged glue omitted from package");
            Check(Included($"{directory}/Source/CoreUObject/Object.generated.cs"), "Fixed source omitted from package");
            Check(!Included($"{directory}/net10.0/BuildHost-net472/host.exe"), "Excluded legacy host was reintroduced");
            Check(File.Exists(Path.Combine(target.BundleDirectory, "UnrealSharpMergedGlue.dll")), "Bundle was not copied into the plugin");
            Check(File.Exists(Path.Combine(target.BindingsRoot, "Source/CoreUObject/Object.generated.cs")), "Fixed source still depends on the temporary HostProject");
            Check(File.Exists(Path.Combine(target.BindingsRoot, "Source/UnrealSharp.EngineGlue.props")), "Exact fixed compile membership was not copied");
        }
        Check(!Included("Binaries/Managed/BuildPlugin/Win64/x64/BalloonEditor/Development/net10.0/UnrealSharp.dll"), "Developer project cache leaked into package");
        Check(!Included("Binaries/Managed/net10.0/UnrealSharp.dll"), "Legacy shared output leaked into package");
        Check(!Included("Binaries/Managed/Targets/legacy/net10.0/UnrealSharp.dll"), "Obsolete ID cache leaked into package");
        Check(Included("UnrealSharp.Target.props"), "Early MSBuild props omitted from package");
        string relocatedPlugin = Path.Combine(fixture, "InstalledPlugin");
        string relativeBundle = Path.GetRelativePath(editor.PluginDirectory, editor.BindingsRoot);
        foreach (string source in Directory.EnumerateFiles(editor.BindingsRoot, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(relocatedPlugin, relativeBundle, Path.GetRelativePath(editor.BindingsRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        GlueTarget relocated = GlueTarget.SelectFrom(Path.Combine(relocatedPlugin, "Binaries/Managed/BuildPlugin"), "Editor", "Win64", "Development", distributionOnly: true)!;
        relocated.MountDistribution(relocatedPlugin, Path.Combine(fixture, "InstalledConsumer", "Consumer.uproject"));
        relocated.CopyBundle(relocated.OutputDirectory);
        Check(File.Exists(Path.Combine(relocated.EngineGlue, "CoreUObject/Object.generated.cs")) &&
            File.Exists(Path.Combine(relocated.OutputDirectory, "UnrealSharpMergedGlue.dll")), "Relocated package still depends on its HostProject");
        Console.WriteLine("PASS: actual Automation publication and UE FileFilter retain Editor/Game/Shipping variants and portable fixed source, and exclude developer caches.");
    }
}
