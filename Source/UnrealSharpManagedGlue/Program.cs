using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using EpicGames.UHT.Tables;
using EpicGames.UHT.Utils;
using UnrealSharpManagedGlue;
using UnrealSharpManagedGlue.PropertyTranslators;
using UnrealSharpManagedGlue.Utilities;

[UnrealHeaderTool]
public static class Program
{
    private static readonly object ExporterLock = new();
    private static readonly ConcurrentDictionary<Assembly, (string Path, string Hash)> ResolvedAssemblySources = new();

    static Program()
    {
        AssemblyLoadContext.Default.Resolving += ResolveAssembly;
    }

    private static Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        string? baseDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);

        if (baseDirectory == null)
        {
            return null;
        }

        string assemblyPath = Path.Combine(baseDirectory, $"{assemblyName.Name}.dll");
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");

        if (File.Exists(pdbPath))
        {
            byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
            using MemoryStream assemblyFile = new(assemblyBytes, writable: false);
            using FileStream pdbFile = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assembly newAssembly = AssemblyLoadContext.Default.LoadFromStream(assemblyFile, pdbFile);
            // Stream-loaded assemblies have an empty Location. Retain the
            // actual source used by this resolver for producer dependencies.
            if (string.IsNullOrEmpty(newAssembly.Location))
            {
                ValidateAssemblyMvid(newAssembly, assemblyBytes, assemblyPath);
                ResolvedAssemblySources.TryAdd(newAssembly, (assemblyPath, Convert.ToHexString(SHA256.HashData(assemblyBytes))));
            }
            return newAssembly;
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    }

    private static void ValidateAssemblyMvid(Assembly assembly, byte[] bytes, string path)
    {
        using PEReader image = new(new MemoryStream(bytes, writable: false));
        MetadataReader metadata = image.GetMetadataReader();
        Guid sourceMvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        if (assembly.ManifestModule.ModuleVersionId != sourceMvid)
            throw new InvalidDataException($"Loaded producer assembly does not match source '{path}'. Retry with a new UHT process.");
    }

    internal static (string Path, string? LoadedHash) GetAssemblySource(Assembly assembly)
    {
        if (!string.IsNullOrEmpty(assembly.Location))
            return (Path.GetFullPath(assembly.Location), null);
        if (ResolvedAssemblySources.TryGetValue(assembly, out var source))
            return (Path.GetFullPath(source.Path), source.Hash);
        throw new InvalidOperationException($"Cannot identify the source file of loaded producer assembly '{assembly.FullName}'.");
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
            ExportBindings();

            FileExporter.CleanOldExportedFiles();
            BuildUtilities.BuildBindings();

            ModuleFactory.SyncModuleProjects();
            BuildUtilities.GenerateUserSolution();
            TaskManager.WaitForTasks();
            if (factory.Session.HasErrors)
            {
                throw new InvalidOperationException("UnrealSharp export did not complete successfully.");
            }
            ExporterValidator.CompleteExport();
        }
        catch
        {
            ExporterProducerContext.InvalidateCompletion();
            throw;
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
        ExporterProducerContext.Reset();
    }

    private static void ExportBindings()
    {
        ConsoleUtilities.Log("Starting C# bindings export...");

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            
            GlueGenerator.GenerateBindings();
            TaskManager.WaitForTasks();

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
