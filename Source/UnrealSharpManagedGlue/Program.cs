using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using EpicGames.UHT.Tables;
using EpicGames.UHT.Utils;
using UnrealSharpManagedGlue;
using UnrealSharpManagedGlue.Utilities;
using UnrealSharpManagedGlue.PropertyTranslators;

[UnrealHeaderTool]
public static class Program
{
    private static readonly object ExporterLock = new();

    static Program()
    {
        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            string? baseDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            
            if (baseDirectory == null)
            {
                return null;
            }
            
            string assemblyPath = Path.Combine(baseDirectory, $"{assemblyName.Name}.dll");

            Assembly newAssembly;
            using FileStream assemblyFile = File.Open(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        
            if (File.Exists(pdbPath))
            {
                using FileStream pdbFile = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                newAssembly = AssemblyLoadContext.Default.LoadFromStream(assemblyFile, pdbFile);
            }
            else
            {
                newAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            }
            
            return newAssembly;
        };
    }
    
    [UhtExporter(Name = "UnrealSharpCore", Description = "Exports C++ to C# code", Options = UhtExporterOptions.Default, ModuleName = "UnrealSharpCore")]
    private static void Main(IUhtExportFactory factory)
    {
        // UBT can run multiple UHT sessions concurrently in the same process for different target groups.
        // Serialize exporter runs to protect shared static state and output files.
        lock (ExporterLock)
        {
            GenerateBindings(factory);
        }
    }

    private static void GenerateBindings(IUhtExportFactory factory)
    {
        ConsoleUtilities.Log($"Initializing UnrealSharp C# bindings generator for {factory.Session.Manifest?.TargetName}...");
        
        try
        {
            ResetRunState();
            GeneratorStatics.Initialize(factory);
            // Source targets share the active glue context per target type. A build
            // for another platform can replace it without changing native headers.
            // Track the context so returning to a cached target regenerates its glue.
            factory.AddExternalDependency(GeneratorStatics.Target.ManifestPath);
            // Files track edits/removals; directories also invalidate UHT for newly added framework inputs.
            foreach (string input in BuildUtilities.GetFrameworkInputDirectories().Concat(BuildUtilities.GetFrameworkInputFiles()))
                factory.AddExternalDependency(input);
            PropertyTranslatorManager.Initialize();
            ModuleFactory.Initialize();
            ExportBindings();

            FileExporter.CleanOldExportedFiles();
            BuildUtilities.PrepareBindings();
            BuildUtilities.BuildUnrealSharpBindings();
            ModuleFactory.SyncModuleProjects();
            BuildUtilities.BuildEnginePluginBindings();
            BuildUtilities.BuildProjectBindings();
            BuildUtilities.PublishRuntimeContext();
            BuildUtilities.GenerateUserSolution();
        }
        finally
        {
            try
            {
                // Failed exports can still have workers using the current run's state.
                TaskManager.WaitForTasks();
            }
            finally
            {
                ResetRunState();
            }
        }
    }

    private static void ResetRunState()
    {
        FileExporter.Reset();
        PackageHeadersTracker.Reset();
        ModuleUtilities.Reset();
        PropertyTranslatorManager.Reset();
        InclusionLists.Reset();
        ModuleFactory.Reset();
        ExporterValidator.Reset();
        UnrealSharpSettingsUtilities.Reset();
        UnrealSharp.Automation.Utilities.UnrealSharpSettingsUtilities.Reset();
        GeneratorStatics.Reset();
    }

    private static void ExportBindings()
    {
        ConsoleUtilities.Log("Starting C# bindings export...");

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            
            GlueGenerator.GenerateBindings();
            TaskManager.WaitForTasks();
            ExporterValidator.CompleteExport();

            stopwatch.Stop();
            
            string timeString = stopwatch.Elapsed.TotalSeconds < 1 ? $"{stopwatch.Elapsed.TotalMilliseconds:F0}ms" : $"{stopwatch.Elapsed.TotalSeconds:F2}s";
            ConsoleUtilities.Log($"Finished exporting C# bindings in {timeString}");
        }
        catch (Exception ex)
        {
            ConsoleUtilities.Log("Critical failure during export process:");
            ConsoleUtilities.Log(ex.ToString());
            GeneratorStatics.Factory.Session.LogError("UnrealSharp C# bindings export failed.");
            throw;
        }
    }
}
