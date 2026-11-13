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
	public static void Reset()
	{
		PendingLayout = null;
		PendingSharedLayout = null;
	}

	private static string? PendingLayout;
	private static string? PendingSharedLayout;
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
		PendingLayout = null;
		PendingSharedLayout = null;
		ConsoleUtilities.Log("Validating exporter...");
		string layoutPath = Path.Combine(GeneratorStatics.Target.Root, "GlueLayout.json");
		string sharedLayoutPath = Path.Combine(GeneratorStatics.Target.EngineGlue, "UnrealSharp.GlueLayout.json");
		string layout = JsonSerializer.Serialize(new
		{
			GeneratorStatics.Target.IntermediateDirectory,
			// Targets with the same type reuse this directory.
			GeneratorStatics.Target.Name,
			GeneratorStatics.Target.Platform,
			GeneratorStatics.Target.Architecture,
			Modules = ModuleUtilities.PackageToModuleInfo.Values
				.OrderBy(module => module.ModuleName, StringComparer.Ordinal)
				.Select(module => new { module.ModuleName, module.AssemblyName })
		});
		// Fixed sources are shared by compatible projects. Project paths and
		// optional plugin membership belong only to the per-target layout above.
		string sharedLayout = JsonSerializer.Serialize(new
		{
			GeneratorStatics.Target.Type,
			GeneratorStatics.Target.Configuration,
			GeneratorStatics.Target.Platform,
			GeneratorStatics.Target.Architecture,
			Modules = ModuleUtilities.PackageToModuleInfo.Values
				.Where(module => module.AssemblyName == "UnrealSharp")
				.OrderBy(module => module.ModuleName, StringComparer.Ordinal)
				.Select(module => new
				{
					module.ModuleName,
					module.GlueOutputDirectory,
					Defines = module.Module.GetModule().PublicDefines.OrderBy(define => define, StringComparer.Ordinal)
				})
		});
		bool layoutChanged = !File.Exists(layoutPath) || File.ReadAllText(layoutPath) != layout ||
			!File.Exists(sharedLayoutPath) || File.ReadAllText(sharedLayoutPath) != sharedLayout;

		if (!HasAssemblyRecentlyChanged() && !layoutChanged)
		{
			PackageHeadersTracker.DeserializeModuleHeaders();
			return;
		}

		ConsoleUtilities.Log("Exporter or assembly membership changed. Re-exporting bindings for this target...");
		CleanModules();
		PendingLayout = layout;
		PendingSharedLayout = sharedLayout;
	}

	public static void CompleteExport()
	{
		if (PendingLayout == null) return;
		// A failed export must retry rather than mark incomplete output current.
		CreateTimestamp();
		File.WriteAllText(Path.Combine(GeneratorStatics.Target.Root, "GlueLayout.json"), PendingLayout);
		File.WriteAllText(Path.Combine(GeneratorStatics.Target.EngineGlue, "UnrealSharp.GlueLayout.json"), PendingSharedLayout);
		PendingLayout = null;
		PendingSharedLayout = null;
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
