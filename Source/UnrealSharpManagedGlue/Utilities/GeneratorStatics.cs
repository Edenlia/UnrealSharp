using System;
using System.IO;
using EpicGames.Core;
using EpicGames.UHT.Types;
using EpicGames.UHT.Utils;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities;
using UnrealSharp.Shared;

namespace UnrealSharpManagedGlue.Utilities;

public static class GeneratorStatics
{
	private static IUhtExportFactory? _factory;
	public static IUhtExportFactory Factory => _factory ?? throw new InvalidOperationException("GeneratorStatics not initialized");

	public static UHTManifest.Module PluginModule => Factory.PluginModule!;
	
	public static ModuleInfo PluginModuleInfo { get; private set; } = null!;
	
	public static UhtClass BlueprintFunctionLibrary { get; private set; } = null!;

	public static string PluginDirectory { get; private set; } = "";
	public static string EngineDirectory => Factory.Session.EngineDirectory!;
	
	public static string ManagedPath { get; private set; } = "";
	
	public static TargetType TargetType { get; private set; }
	public static UnrealTargetConfiguration TargetConfiguration;
	public static UnrealTargetPlatform TargetPlatform { get; private set; }

	// Capture at the call site so existing initialization order and mutable
	// build settings retain their lifetime and behavior.
	public static GlueTarget CreateGlueTarget() => new(TargetType.ToString(), TargetConfiguration.ToString(),
		Factory.Session.ProjectDirectory, PluginDirectory)
	{
		Name = Factory.Session.Manifest!.TargetName,
		Platform = TargetPlatform.ToString(),
		Architecture = ScriptGeneratorUtilities.TryGetPluginStringDefine("TARGET_ARCHITECTURE"),
		ProjectFile = Factory.Session.ProjectFile,
		BuildAllModules = ScriptGeneratorUtilities.TryGetPluginIntDefine("TARGET_BUILD_ALL_MODULES") != 0
	};
	
	public static void Initialize(IUhtExportFactory factory)
	{
		_factory = factory;
		
		PluginDirectory = ScriptGeneratorUtilities.TryGetPluginStringDefine("PLUGIN_PATH");
		UnrealSharpSettingsUtilities.InitializeConfigFile(Factory.Session.ProjectDirectory!, PluginDirectory);
		
		TargetType = (TargetType) ScriptGeneratorUtilities.TryGetPluginIntDefine("TARGET_TYPE");
		TargetConfiguration = (UnrealTargetConfiguration) ScriptGeneratorUtilities.TryGetPluginIntDefine("TARGET_CONFIGURATION");
		TargetPlatform = UnrealTargetPlatform.Parse(ScriptGeneratorUtilities.TryGetPluginStringDefine("TARGET_PLATFORM"));
		
		ManagedPath = Path.Combine(PluginDirectory, "Managed");
		
		BlueprintFunctionLibrary = (Factory.Session.FindType(null, UhtFindOptions.SourceName | UhtFindOptions.Class, "UBlueprintFunctionLibrary") as UhtClass)!;
		
		PluginModuleInfo = ModuleUtilities.GetModuleInfo(factory.PluginModule!.Name);

		PathUtilities.InitPaths(factory.Session.EngineDirectory!);
	}
}
