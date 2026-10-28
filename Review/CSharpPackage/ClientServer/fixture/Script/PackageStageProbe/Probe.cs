using UnrealSharp.Attributes;
using UnrealSharp.Engine.Core.Modules;
namespace PackageStageProbe;
[UModule]
public class FPackageStageProbe : IModuleInterface
{
    public void StartupModule()
    {
#if WITH_CLIENT && WITH_DEVELOPMENT
        const string target = "Client/Development";
#elif WITH_SERVER && WITH_DEVELOPMENT
        const string target = "Server/Development";
#elif WITH_EDITOR
        const string target = "Editor";
#else
        const string target = "Unexpected";
#endif
        const string version = "client-server-ui-20261028";
        string directory = UnrealSharp.UnrealSharpUtilities.UCSPathsBlueprintFunctionLibrary.UserAssemblyDirectory;
        string file = Path.Combine(directory, "package-stage.txt");
        string content = File.Exists(file) ? File.ReadAllText(file).Trim() : "missing";
        Console.WriteLine($"PACKAGE_STAGE_PROBE version={version} target={target} content={content} directory={directory}");
        string? report = Environment.GetEnvironmentVariable("PACKAGE_STAGE_PROBE_RESULT");
        if (!string.IsNullOrEmpty(report))
            File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { version, target, content, directory, runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), process = Environment.ProcessPath }));
    }
    public void ShutdownModule() { }
}
