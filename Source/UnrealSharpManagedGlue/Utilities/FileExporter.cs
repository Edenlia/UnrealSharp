using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using EpicGames.UHT.Types;
using UnrealSharpManagedGlue.PropertyTranslators;
using UnrealSharpManagedGlue.SourceGeneration;

namespace UnrealSharpManagedGlue.Utilities;

public static class FileExporter
{
	private static readonly object[] FileLocks = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
	private static readonly ConcurrentDictionary<string, byte> AffectedFiles = new(StringComparer.OrdinalIgnoreCase);
    
	public static bool HasModifiedEngineGlue { get; private set; }

	public static void Reset()
	{
		AffectedFiles.Clear();
		HasModifiedEngineGlue = false;
	}

	public static void SaveGlueToDisk(UhtType type, GeneratorStringBuilder stringBuilder)
	{
		string directory = type.Package.GetModuleInfo().GlueOutputDirectory;
		SaveGlueToDisk(type.Package, directory, type.EngineName, stringBuilder.ToString());
	}

	public static string GetFilePath(string typeName, string directory)
	{
		return Path.Combine(directory, $"{typeName}.generated.cs");
	}

	public static void SaveGlueToDisk(UhtPackage package, string directory, string typeName, string text)
	{
		string absoluteFilePath = GetFilePath(typeName, directory);
		// Keep both the comparison and write serialized for the same output path.
		uint lockIndex = unchecked((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(absoluteFilePath)) % (uint)FileLocks.Length;
		lock (FileLocks[lockIndex])
		{
			AffectedFiles.TryAdd(absoluteFilePath, 0);
			if (File.Exists(absoluteFilePath) && File.ReadAllText(absoluteFilePath) == text)
			{
				return;
			}

			if (!Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			File.WriteAllText(absoluteFilePath, text);
			package.GetModuleInfo().MarkDirty();

			if (package.IsPartOfEngine())
			{
				HasModifiedEngineGlue = true;
			}
		}
	}

	public static void AddUnchangedType(UhtType type)
	{
		string engineName = type is UhtFunction function ? DelegateBasePropertyTranslator.GetDelegateName(function) : type.EngineName;

		string directory = type.Package.GetPackageOutputDirectory();

		AffectedFiles.TryAdd(GetFilePath(engineName, directory), 0);

		if (type is UhtStruct uhtStruct && uhtStruct.Functions.Any(f => f.HasMetadata("ExtensionMethod")))
		{
			AffectedFiles.TryAdd(GetFilePath($"{engineName}_Extensions", directory), 0);
		}
	}

	public static void CleanOldExportedFiles()
	{
		ConsoleUtilities.Log("Cleaning up old generated C# glue files...");

		foreach (ModuleInfo plugin in ModuleUtilities.PackageToModuleInfo.Values)
		{
			CleanOldFilesInDirectories(plugin.GlueOutputDirectory);
		}
	}

	public static void CleanGeneratedFolder(string path)
	{
		if (!Directory.Exists(path))
		{
			return;
		}
    
		DirectoryInfo root = new DirectoryInfo(path);
		foreach (FileSystemInfo item in root.GetFileSystemInfos())
		{
			if (item is DirectoryInfo dir)
			{
				dir.Delete(true);
			}
			else
			{
				item.Delete();
			}
		}
	}

	private static void CleanOldFilesInDirectories(string path)
	{
		if (!Directory.Exists(path))
		{
			return;
		}

		if (!PackageHeadersTracker.HasModuleBeenExported(Path.GetFileName(path)))
		{
			return;
		}
        
		foreach (string directory in Directory.GetDirectories(path))
		{
			CleanOldFilesInDirectories(directory);
		}
        
		string[] files = Directory.GetFiles(path);
		foreach (string file in files)
		{
			if (Path.GetExtension(file) != ".cs" || AffectedFiles.ContainsKey(file))
			{
				continue;
			}
            
			try 
			{
				File.Delete(file);
			}
			catch (IOException exception) 
			{
				ConsoleUtilities.Log($"Failed to delete file {file}: {exception.Message}");
			}
		}

		if (!Directory.Exists(path) || Directory.GetFileSystemEntries(path).Length != 0)
		{
			return;
		}
        
		try 
		{
			Directory.Delete(path, false);
		}
		catch (IOException exception)
		{
			ConsoleUtilities.Log($"Failed to delete directory {path}: {exception.Message}");
		}
	}
}
