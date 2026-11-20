using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EpicGames.UHT.Types;
using UnrealSharpManagedGlue.Utilities;

namespace UnrealSharpManagedGlue;

public class PackageHeaders
{
    public Dictionary<string, DateTime> HeaderToWriteTime { get; set; } = new();
    public bool HasBeenExported;
}

public static class PackageHeadersTracker
{
    const string PackageDataFileName = "UnrealSharpPackagesData.json";
    
    private static Dictionary<string, PackageHeaders?> _packageHeadersWriteRecord = new();

    public static void Reset() => _packageHeadersWriteRecord.Clear();
    
    public static void DeserializeModuleHeaders()
    {
        if (_packageHeadersWriteRecord.Count > 0)
        {
            return;
        }
        
        if (!Directory.Exists(GeneratorStatics.PluginDirectory))
        {
            return;
        }

        string outputPath = Path.Combine(GeneratorStatics.PluginModule.OutputDirectory, PackageDataFileName);

        if (!File.Exists(outputPath))
        {
            return;
        }

        using FileStream fileStream = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Dictionary<string, PackageHeaders>? jsonValue = JsonSerializer.Deserialize<Dictionary<string, PackageHeaders>>(fileStream);
        fileStream.Close();

        if (jsonValue == null)
        {
            return;
        }
        
        _packageHeadersWriteRecord = new Dictionary<string, PackageHeaders?>(jsonValue!);
    }
    
    public static void SerializeModuleData()
    {
        string outputPath = Path.Combine(GeneratorStatics.PluginModule.OutputDirectory, PackageDataFileName);
        using FileStream filestream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        JsonSerializer.Serialize(filestream, _packageHeadersWriteRecord);
        filestream.Close();
    }
    
    public static bool HasModuleBeenExported(string packageName)
    {
        if (_packageHeadersWriteRecord.TryGetValue(packageName, out PackageHeaders? headers))
        {
            return headers != null && headers.HasBeenExported;
        }

        return false;
    }
    
    public static bool UpdateHeaderWriteTime(string packageName, UhtHeaderFile header)
    {
        if (!_packageHeadersWriteRecord.TryGetValue(packageName, out PackageHeaders? headers) || headers == null)
        {
            headers = new PackageHeaders();
            _packageHeadersWriteRecord[packageName] = headers;
        }

        DateTime currentWriteTime = File.GetLastWriteTimeUtc(header.FilePath);
        bool changed = !headers.HeaderToWriteTime.TryGetValue(header.FilePath, out DateTime previousWriteTime) ||
                       previousWriteTime != currentWriteTime;
        headers.HeaderToWriteTime[header.FilePath] = currentWriteTime;
        headers.HasBeenExported = true;
        return changed;
    }
}
