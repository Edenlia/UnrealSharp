using UnrealSharpManagedGlue.Exporters;
using UnrealSharpManagedGlue.Utilities;

namespace UnrealSharpManagedGlue;

public static class GlueGenerator
{
    public static void GenerateBindings(TaskManager tasks, FileExporter files, PackageHeadersTracker headers)
    {
        ExporterValidator.ValidateExporter(headers);
        
        ConsoleUtilities.Log("Generating C# bindings...");
        PackageExporter.ExportPackages(tasks, files, headers);
        PreprocessorExporter.GenerateMSBuildProps();
        headers.SerializeModuleData();
    }
}
