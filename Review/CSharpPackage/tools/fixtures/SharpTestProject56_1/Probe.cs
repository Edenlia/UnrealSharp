using UnrealSharp.Attributes;
using UnrealSharp.Engine.Core.Modules;
namespace PackageStageProbe;
[UModule]
public class FPackageStageProbe : IModuleInterface
{
    public void StartupModule()
    {
#if WITH_GAME && WITH_DEVELOPMENT
        const string target = "Game/Development";
#elif WITH_EDITOR
        const string target = "Editor";
#else
        const string target = "Unexpected";
#endif
        const int version = 1;
        string directory = UnrealSharp.UnrealSharpUtilities.UCSPathsBlueprintFunctionLibrary.UserAssemblyDirectory;
        string path = Path.Combine(directory, "package-stage.txt");
        string content = File.Exists(path) ? File.ReadAllText(path).Trim() : "missing";
        Console.WriteLine($"PACKAGE_STAGE_PROBE version={version} target={target} content={content} directory={directory}");
        string? report = Environment.GetEnvironmentVariable("PACKAGE_STAGE_PROBE_RESULT");
        if (!string.IsNullOrEmpty(report))
            File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { version, target, content, directory, runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), process = Environment.ProcessPath }));
    }
    public void ShutdownModule() { }
}
