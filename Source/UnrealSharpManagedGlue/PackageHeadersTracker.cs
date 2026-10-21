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

public class PackageHeadersTracker
{
    const string PackageDataFileName = "UnrealSharpPackagesData.json";
    
    private Dictionary<string, PackageHeaders?> _packageHeadersWriteRecord = new();
    private readonly string OutputPath;

    public PackageHeadersTracker(string outputDirectory)
    {
        OutputPath = Path.Combine(outputDirectory, PackageDataFileName);
    }
    
    public void DeserializeModuleHeaders()
    {
        if (_packageHeadersWriteRecord.Count > 0)
        {
            return;
        }
        
        if (!File.Exists(OutputPath))
        {
            return;
        }

        using FileStream fileStream = new FileStream(OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Dictionary<string, PackageHeaders>? jsonValue = JsonSerializer.Deserialize<Dictionary<string, PackageHeaders>>(fileStream);
        fileStream.Close();

        if (jsonValue == null)
        {
            return;
        }
        
        _packageHeadersWriteRecord = new Dictionary<string, PackageHeaders?>(jsonValue!);
    }
    
    public void SerializeModuleData()
    {
        using FileStream filestream = new FileStream(OutputPath, FileMode.Create, FileAccess.Write);
        JsonSerializer.Serialize(filestream, _packageHeadersWriteRecord);
        filestream.Close();
    }
    
    public bool HasModuleBeenExported(string packageName)
    {
        if (_packageHeadersWriteRecord.TryGetValue(packageName, out PackageHeaders? headers))
        {
            return headers != null && headers.HasBeenExported;
        }

        return false;
    }
    
    public bool HasHeaderFileChanged(string packageName, UhtHeaderFile headerPath)
    {
        if (!_packageHeadersWriteRecord.TryGetValue(packageName, out PackageHeaders? headers))
        {
            return true;
        }
        
        if (headers == null || !headers.HeaderToWriteTime.TryGetValue(headerPath.FilePath, out DateTime recordedWriteTime))
        {
            return true;
        }

        return recordedWriteTime != File.GetLastWriteTimeUtc(headerPath.FilePath);
    }
    
    public void RecordPackageHeadersWriteTime(string packageName, IEnumerable<UhtHeaderFile> headerPaths)
    {
        foreach (UhtHeaderFile headerPath in headerPaths)
        {
            RecordHeaderWriteTime(packageName, headerPath);
        }
    }

    private void RecordHeaderWriteTime(string packageName, UhtHeaderFile headerPath)
    {
        DateTime currentWriteTime = File.GetLastWriteTimeUtc(headerPath.FilePath);
        
        if (!_packageHeadersWriteRecord.TryGetValue(packageName, out PackageHeaders? headers) || headers == null)
        {
            headers = new PackageHeaders();
            _packageHeadersWriteRecord[packageName] = headers;
        }
        
        headers.HeaderToWriteTime[headerPath.FilePath] = currentWriteTime;
        headers.HasBeenExported = true;
    }
}
