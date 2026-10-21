using System;
using System.Collections.Generic;
using System.IO;
using EpicGames.Core;
using EpicGames.UHT.Types;
using UnrealSharpManagedGlue.Model;
using UnrealSharpManagedGlue.PropertyTranslators;
using UnrealSharpManagedGlue.Utilities;

namespace UnrealSharpManagedGlue.Exporters;

public static class PackageExporter
{
    public static void ExportPackages(TaskManager tasks, FileExporter files, PackageHeadersTracker headers)
    {
        foreach (ModuleInfo moduleInfo in ModuleUtilities.PackageToModuleInfo.Values)
        {
            ExportPackage(moduleInfo.Module, tasks, files, headers);
        }
        
        tasks.WaitForTasks();

        // UHT records worker exceptions on the session instead of faulting the tasks.
        if (GeneratorStatics.Factory.Session.HasErrors)
        {
            throw new InvalidOperationException("UnrealSharp C# bindings export failed.");
        }
    }
    
    private static void ExportPackage(UhtPackage package, TaskManager tasks, FileExporter files, PackageHeadersTracker headers)
    {
        string packageName = package.GetModuleShortName();
        string generatedPath = package.GetPackageOutputDirectory();
        bool generatedGlueFolderExists = Directory.Exists(generatedPath);

        int childrenCount = package.Children.Count;
        UhtHeaderFile[] processedHeaders = new UhtHeaderFile[childrenCount];
        
        for (int i = 0; i < childrenCount; i++)
        {
            UhtType child = package.Children[i];
            bool hasChanged = !generatedGlueFolderExists || headers.HasHeaderFileChanged(packageName, child.HeaderFile);
            Action<UhtType> processAction = hasChanged ? type => ExportType(type, tasks, files) : files.AddUnchangedType;

            processAction(child);

            foreach (UhtType type in child.Children)
            {
                processAction(type);
            }
            
            processedHeaders[i] = child.HeaderFile;
        }
        
        headers.RecordPackageHeadersWriteTime(packageName, processedHeaders);
    }

    private static void ExportType(UhtType type, TaskManager tasks, FileExporter files)
    {
        SpecialStructInfo specialStructs = PropertyTranslatorManager.SpecialTypeInfo.Structs;

        if (type.CanSkipType() || specialStructs.SkippedTypes.Contains(type.SourceName))
        {
            return;
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
                    exportAction = () => InterfaceExporter.ExportInterface(classObj, files);
                }
            }
            else
            {
                exportAction = () => ClassExporter.ExportClass(classObj, isManualExport, files);
            }
        }
        else if (type is UhtEnum enumObj)
        {
            if (isManualExport)
            {
                return;
            }
            
            exportAction = () => EnumExporter.ExportEnum(enumObj, files);
        }
        else if (type is UhtScriptStruct structObj)
        {
            bool isBlittable = specialStructs.BlittableTypes.TryGetValue(structObj.SourceName, out BlittableStructInfo info) && info.ManagedType is not null;
            exportAction = () => StructExporter.ExportStruct(structObj, isBlittable, files);
        }
        else if (IsDelegateType(type))
        {
            UhtFunction delegateFunction = (UhtFunction)type;
            if (!isManualExport && delegateFunction.ReturnProperty == null && delegateFunction.CanExportParameters())
            {
                exportAction = () => DelegateExporter.ExportDelegate(delegateFunction, files);
            }
        }

        if (exportAction != null)
        {
            tasks.StartTask(_ => exportAction());
        }
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
