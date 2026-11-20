using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using EpicGames.UHT.Tables;
using EpicGames.UHT.Utils;
using Microsoft.Extensions.Logging;
using UnrealBuildTool;
using UnrealSharpManagedGlue;
using UnrealSharpManagedGlue.PropertyTranslators;
using UnrealSharpManagedGlue.Utilities;

[UnrealHeaderTool]
public static class Program
{
    private static readonly object ExporterLock = new();
    private const int ExportStageCount = 5;

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
            RunExporter(factory);
        }
    }

    private static void RunExporter(IUhtExportFactory factory)
    {
        ConsoleUtilities.Log("Initializing UnrealSharp C# bindings generator...");

        try
        {
            ResetRunState();
            GeneratorStatics.Initialize(factory);
            PropertyTranslatorManager.Initialize();
            RunStage(1, "Export C# bindings", () => ExportBindings((completed, total) =>
            {
                ReportProgress($"Export C# bindings ({completed}/{total} modules)", (double)completed / total);
            }));
            RunStage(2, "Clean up obsolete bindings", FileExporter.CleanOldExportedFiles);
            RunStage(3, "Build UnrealSharp managed assemblies", BuildUtilities.BuildBindings);
            RunStage(4, "Synchronize project glue", ModuleFactory.SyncModuleProjects);
            RunStage(5, "Prepare C# solution", BuildUtilities.GenerateUserSolution);
            ReportProgress("C# setup complete", ExportStageCount);

            void RunStage(int stage, string message, Action action)
            {
                ConsoleUtilities.Log($"[{stage}/{ExportStageCount}] {message}...");
                ReportProgress($"[{stage}/{ExportStageCount}] {message}", stage - 1);
                Stopwatch stopwatch = Stopwatch.StartNew();
                action();
                ConsoleUtilities.Log($"[{stage}/{ExportStageCount}] {message} finished in {stopwatch.Elapsed.TotalSeconds:F2}s.");
            }

            void ReportProgress(string message, double completedStages)
            {
                if (ProgressWriter.bWriteMarkup)
                {
                    int percent = (int)Math.Round(completedStages * 100 / ExportStageCount);
                    factory.Session.Logger.LogInformation("@progress 'UnrealSharp: {Message}' {Percent}%", message, percent);
                }
            }
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
        UnrealSharpSettingsUtilities.Reset();
        UnrealSharp.Automation.Utilities.UnrealSharpSettingsUtilities.Reset();
        GeneratorStatics.Reset();
    }

    private static void ExportBindings(Action<int, int> reportProgress)
    {
        try
        {
            GlueGenerator.GenerateBindings(reportProgress);
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
