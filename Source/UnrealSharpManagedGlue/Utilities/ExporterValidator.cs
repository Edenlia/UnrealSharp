using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace UnrealSharpManagedGlue.Utilities;

public static class ExporterValidator
{
	private static string AssemblyModificationTimeFile => Path.Combine(GeneratorStatics.PluginModule.OutputDirectory, "GlueGeneratorTimestamp");
	private static string HeaderExclusionsFile => Path.Combine(GeneratorStatics.PluginModule.OutputDirectory, "GlueGeneratorHeaderExclusions.json");

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

		string exclusions = JsonSerializer.Serialize(UnrealSharpSettingsUtilities.ExcludedHeaderPaths);
		bool exclusionsChanged = !File.Exists(HeaderExclusionsFile) || File.ReadAllText(HeaderExclusionsFile) != exclusions;
		bool producerMatches = ExporterProducerContext.Initialize();
		if (!HasAssemblyRecentlyChanged() && !exclusionsChanged && producerMatches)
		{
			PackageHeadersTracker.DeserializeModuleHeaders();
			ExporterProducerContext.PrepareIncrementalExport();
			return;
		}

		ConsoleUtilities.Log("Exporter is stale. Re-exporting all bindings...");
		ExporterProducerContext.CleanGeneratedOutputs();
	}

	public static void CompleteExport()
	{
		CreateTimestamp();
		ExporterProducerContext.WriteIfDifferent(HeaderExclusionsFile, JsonSerializer.Serialize(UnrealSharpSettingsUtilities.ExcludedHeaderPaths));
		ExporterProducerContext.Complete();
	}

	private static void CreateTimestamp()
	{
		string executingAssemblyPath = Assembly.GetExecutingAssembly().Location;
		DateTime executingAssemblyLastWriteTime = File.GetLastWriteTimeUtc(executingAssemblyPath);

		string? directory = Path.GetDirectoryName(AssemblyModificationTimeFile);
		if (!string.IsNullOrEmpty(directory))
		{
			ExporterProducerContext.EnsureSafePath(directory);
			Directory.CreateDirectory(directory);
		}

		ExporterProducerContext.WriteIfDifferent(AssemblyModificationTimeFile, executingAssemblyLastWriteTime.ToString("o"));
	}
}
