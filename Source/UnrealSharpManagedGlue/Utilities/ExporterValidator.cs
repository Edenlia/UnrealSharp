using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text.Json;

namespace UnrealSharpManagedGlue.Utilities;

public static class ExporterValidator
{
	private static string AssemblyModificationTimeFile => Path.Combine(GeneratorStatics.Target.Root, "GlueGeneratorTimestamp");

	static bool HasAssemblyRecentlyChanged()
	{
		string executingAssemblyPath = Assembly.GetExecutingAssembly().Location;
		DateTime executingAssemblyLastWriteTime = File.GetLastWriteTimeUtc(executingAssemblyPath);
		
		if (!File.Exists(AssemblyModificationTimeFile))
		{
			return true;
		}

		string savedTimestamp = File.ReadAllText(AssemblyModificationTimeFile);
		if (!DateTime.TryParse(savedTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime savedTimestampUtc))
		{
			return true;
		}

		return executingAssemblyLastWriteTime > savedTimestampUtc;
	}

	public static void ValidateExporter()
	{
		ConsoleUtilities.Log("Validating exporter...");
		string layoutPath = Path.Combine(GeneratorStatics.Target.Root, "GlueLayout.json");
		string layout = JsonSerializer.Serialize(ModuleUtilities.PackageToModuleInfo.Values
			.OrderBy(module => module.ModuleName, StringComparer.Ordinal)
			.Select(module => new { module.ModuleName, module.AssemblyName }));
		bool layoutChanged = !File.Exists(layoutPath) || File.ReadAllText(layoutPath) != layout;

		if (!HasAssemblyRecentlyChanged() && !layoutChanged)
		{
			PackageHeadersTracker.DeserializeModuleHeaders();
			return;
		}

		ConsoleUtilities.Log("Exporter or assembly membership changed. Re-exporting bindings for this target...");
		CleanModules();
		CreateTimestamp();
		File.WriteAllText(layoutPath, layout);
	}

	private static void CreateTimestamp()
	{
		string executingAssemblyPath = Assembly.GetExecutingAssembly().Location;
		DateTime executingAssemblyLastWriteTime = File.GetLastWriteTimeUtc(executingAssemblyPath);

		string? directory = Path.GetDirectoryName(AssemblyModificationTimeFile);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		File.WriteAllText(AssemblyModificationTimeFile, executingAssemblyLastWriteTime.ToString("o"));
	}

	private static void CleanModules()
	{
		IEnumerable<ModuleInfo> modules = ModuleUtilities.PackageToModuleInfo.Values;

		foreach (ModuleInfo module in modules)
		{
			FileExporter.CleanGeneratedFolder(module.Module.GetPackageOutputDirectory());

			if (module.IsPartOfEngine || !File.Exists(module.CsProjPath))
			{
				continue;
			}

			File.Delete(module.CsProjPath);
		}
	}
}
