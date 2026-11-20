using UnrealSharpManagedGlue.Exporters;
using UnrealSharpManagedGlue.Utilities;
using System;

namespace UnrealSharpManagedGlue;

public static class GlueGenerator
{
    public static void GenerateBindings(Action<int, int> reportProgress)
    {
        ExporterValidator.ValidateExporter();
        
        ConsoleUtilities.Log("Generating C# bindings...");
        PackageExporter.ExportPackages(reportProgress);
        PreprocessorExporter.GenerateMSBuildProps();
        PackageHeadersTracker.SerializeModuleData();
    }
}
