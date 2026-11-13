using System;
using System.IO;
using EpicGames.Core;
using EpicGames.UHT.Types;
using EpicGames.UHT.Utils;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities;
using UnrealSharp.Shared;
using UnrealSharpManagedGlue.PropertyTranslators;

namespace UnrealSharpManagedGlue.Utilities;

public static class GeneratorStatics
{
	public static void Reset()
	{
		_factory = null;
		PluginModuleInfo = null!;
		BlueprintFunctionLibrary = null!;
		PluginDirectory = "";
		ManagedPath = "";
		TargetType = default;
		TargetConfiguration = default;
		TargetPlatform = default;
		Target = null!;
		PluginPolicy = null!;
	}

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
	public static GlueTarget Target { get; private set; } = null!;
	public static PluginGluePolicy PluginPolicy { get; private set; } = null!;
	
	public static void Initialize(IUhtExportFactory factory)
	{
		if (_factory != null)
		{
			factory.Session.LogError("UnrealSharp generator data was initialized more than once for this UHT session.");
			throw new InvalidOperationException("GeneratorStatics already initialized");
		}

		_factory = factory;
		
		PluginDirectory = ScriptGeneratorUtilities.TryGetPluginStringDefine("PLUGIN_PATH");
		UnrealSharpSettingsUtilities.InitializeConfigFile(Factory.Session.ProjectDirectory ?? PluginDirectory, PluginDirectory);
		
		TargetType = (TargetType) ScriptGeneratorUtilities.TryGetPluginIntDefine("TARGET_TYPE");
		TargetConfiguration = (UnrealTargetConfiguration) ScriptGeneratorUtilities.TryGetPluginIntDefine("TARGET_CONFIGURATION");
		TargetPlatform = UnrealTargetPlatform.Parse(ScriptGeneratorUtilities.TryGetPluginStringDefine("TARGET_PLATFORM"));
		Target = new GlueTarget
		{
			Name = Factory.Session.Manifest!.TargetName,
			Type = TargetType.ToString(), Platform = TargetPlatform.ToString(), Configuration = TargetConfiguration.ToString(),
			Architecture = ScriptGeneratorUtilities.TryGetPluginStringDefine("TARGET_ARCHITECTURE"),
			BuildAllModules = ScriptGeneratorUtilities.TryGetPluginIntDefine("TARGET_BUILD_ALL_MODULES") != 0,
			Distribution = ScriptGeneratorUtilities.TryGetPluginIntDefine("UNREALSHARP_DISTRIBUTION") != 0,
			PluginDirectory = Path.GetFullPath(PluginDirectory),
			ProjectDirectory = Path.GetFullPath(Factory.Session.ProjectDirectory ?? EngineDirectory),
			ProjectFile = Factory.Session.ProjectFile ?? string.Empty
		};
		Target.IntermediateDirectory = PathUtilities.GetTargetIntermediateDirectory(Target.ProjectDirectory,
			Target.Type);
		Directory.CreateDirectory(Target.Root);
		Directory.CreateDirectory(Target.EngineGlue);
		PathUtilities.InitPaths(Directory.GetParent(EngineDirectory)!.FullName);
		PluginPolicy = new PluginGluePolicy();
		
		ManagedPath = Path.Combine(PluginDirectory, "Managed");
		
		BlueprintFunctionLibrary = (Factory.Session.FindType(null, UhtFindOptions.SourceName | UhtFindOptions.Class, "UBlueprintFunctionLibrary") as UhtClass)!;
		
		ModuleUtilities.Initialize();
		PluginModuleInfo = ModuleUtilities.GetModuleInfo(factory.PluginModule!.Name);

	}
}
