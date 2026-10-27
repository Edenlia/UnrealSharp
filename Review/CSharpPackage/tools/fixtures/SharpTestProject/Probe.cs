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
        string directory = UnrealSharp.UnrealSharpUtilities.UCSPathsBlueprintFunctionLibrary.UserAssemblyDirectory;
        string content = File.Exists(Path.Combine(directory, "package-stage.txt")) ? File.ReadAllText(Path.Combine(directory, "package-stage.txt")).Trim() : "missing";
        string observation = $"PACKAGE_STAGE_PROBE version=4 target={target} content={content} directory={directory}";
        Console.WriteLine(observation);
        string? result = Environment.GetEnvironmentVariable("PACKAGE_STAGE_PROBE_RESULT");
        if (!string.IsNullOrEmpty(result)) File.WriteAllText(result, observation);
    }
    public void ShutdownModule() { }
}
