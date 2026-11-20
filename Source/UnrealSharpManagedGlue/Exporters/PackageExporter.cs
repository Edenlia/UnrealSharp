using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using EpicGames.Core;
using EpicGames.UHT.Types;
using UnrealSharpManagedGlue.Model;
using UnrealSharpManagedGlue.PropertyTranslators;
using UnrealSharpManagedGlue.Utilities;

namespace UnrealSharpManagedGlue.Exporters;

public static class PackageExporter
{
    private const int ExportBatchSize = 32;

    public static void ExportPackages(Action<int, int> reportProgress)
    {
        int total = ModuleUtilities.PackageToModuleInfo.Count;
        int completed = 0;
        object progressLock = new();
        Stopwatch progressTimer = Stopwatch.StartNew();
        ConsoleUtilities.Log($"[0/{total}] Processing C# binding modules...");

        foreach (ModuleInfo moduleInfo in ModuleUtilities.PackageToModuleInfo.Values)
        {
            List<Task?> tasks = ExportPackage(moduleInfo.Module);
            if (tasks.Count == 0)
            {
                CompletePackage(moduleInfo.ModuleName);
            }
            else
            {
                TaskManager.StartTask(_ => CompletePackage(moduleInfo.ModuleName), tasks);
            }
        }
        
        TaskManager.WaitForTasks();
        if (GeneratorStatics.Factory.Session.HasErrors)
        {
            throw new InvalidOperationException("UnrealSharp export tasks failed.");
        }

        ConsoleUtilities.Log($"[{total}/{total}] Processed C# binding modules.");

        void CompletePackage(string moduleName)
        {
            lock (progressLock)
            {
                completed++;
                if (completed < total && progressTimer.ElapsedMilliseconds >= 250)
                {
                    reportProgress(completed, total);
                    ConsoleUtilities.Log($"[{completed}/{total}] Processed C# binding module {moduleName}.");
                    progressTimer.Restart();
                }
            }
        }
    }
    
    private static List<Task?> ExportPackage(UhtPackage package)
    {
        string packageName = package.GetModuleShortName();
        string generatedPath = package.GetPackageOutputDirectory();
        bool generatedGlueFolderExists = Directory.Exists(generatedPath);
        Dictionary<UhtHeaderFile, bool> changedHeaders = new();
        List<Task?> tasks = new();
        List<Action> batch = new(ExportBatchSize);

        foreach (UhtType child in package.Children)
        {
            UhtHeaderFile header = child.HeaderFile;
            if (!changedHeaders.TryGetValue(header, out bool hasChanged))
            {
                hasChanged = PackageHeadersTracker.UpdateHeaderWriteTime(packageName, header) || !generatedGlueFolderExists;
                changedHeaders.Add(header, hasChanged);
            }

            ProcessType(child, hasChanged);
            foreach (UhtType type in child.Children)
            {
                ProcessType(type, hasChanged);
            }
        }

        if (batch.Count > 0)
        {
            ScheduleBatch();
        }

        return tasks;

        void ProcessType(UhtType type, bool hasChanged)
        {
            if (!hasChanged)
            {
                FileExporter.AddUnchangedType(type);
                return;
            }

            Action? export = GetExportAction(type);
            if (export == null)
            {
                return;
            }

            batch.Add(export);
            if (batch.Count == ExportBatchSize)
            {
                ScheduleBatch();
            }
        }

        void ScheduleBatch()
        {
            List<Action> scheduled = batch;
            tasks.Add(TaskManager.StartTask(_ =>
            {
                foreach (Action export in scheduled)
                {
                    export();
                }
            }));
            batch = new List<Action>(ExportBatchSize);
        }
    }

    private static Action? GetExportAction(UhtType type)
    {
        SpecialStructInfo specialStructs = PropertyTranslatorManager.SpecialTypeInfo.Structs;

        if (type.CanSkipType() || specialStructs.SkippedTypes.Contains(type.SourceName))
        {
            return null;
        }

        bool isManualExport = specialStructs.BlittableTypes.ContainsKey(type.SourceName);
        Action? exportAction = null;

        if (type is UhtClass classObj)
        {
            if (classObj.HasAllFlags(EClassFlags.Interface))
            {
                // Prefer the reflected root, keeping the native root as a fallback for
                // older UHT versions and configurations that exclude the reflected root.
                if (classObj.ClassType is UhtClassType.Interface ||
                    (type == GeneratorStatics.Factory.Session.IInterface && !CanExportReflectedRootInterface(specialStructs)))
                {
                    exportAction = () => InterfaceExporter.ExportInterface(classObj);
                }
            }
            else
            {
                exportAction = () => ClassExporter.ExportClass(classObj, isManualExport);
            }
        }
        else if (type is UhtEnum enumObj)
        {
            if (isManualExport)
            {
                return null;
            }
            
            exportAction = () => EnumExporter.ExportEnum(enumObj);
        }
        else if (type is UhtScriptStruct structObj)
        {
            bool isBlittable = specialStructs.BlittableTypes.TryGetValue(structObj.SourceName, out BlittableStructInfo info) && info.ManagedType is not null;
            exportAction = () => StructExporter.ExportStruct(structObj, isBlittable);
        }
        else if (IsDelegateType(type))
        {
            UhtFunction delegateFunction = (UhtFunction)type;
            if (!isManualExport && delegateFunction.ReturnProperty == null && delegateFunction.CanExportParameters())
            {
                exportAction = () => DelegateExporter.ExportDelegate(delegateFunction);
            }
        }

        return exportAction;
    }

    private static bool CanExportReflectedRootInterface(SpecialStructInfo specialStructs)
    {
        UhtClass rootInterface = GeneratorStatics.Factory.Session.UInterface;
        return rootInterface.ClassType is UhtClassType.Interface &&
               !rootInterface.CanSkipType() &&
               !specialStructs.SkippedTypes.Contains(rootInterface.SourceName);
    }

    private static bool IsDelegateType(UhtType type)
    {
        return type.EngineType == UhtEngineType.Delegate 
#if UE_5_7_OR_LATER
               || type.EngineType == UhtEngineType.SparseDelegate
#endif
               ;
    }
}
