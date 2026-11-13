using System.IO;
using UnrealBuildTool;
using System;
using System.Linq;

public class UnrealSharpCore : ModuleRules
{
	public UnrealSharpCore(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		
		PublicDefinitions.Add("PLUGIN_PATH=" + PluginDirectory.Replace("\\","/"));
		PublicDefinitions.Add("TARGET_TYPE=" + (int)Target.Type);
		PublicDefinitions.Add("TARGET_CONFIGURATION=" + (int)Target.Configuration);
		PublicDefinitions.Add("TARGET_PLATFORM=" + Target.Platform);
		PublicDefinitions.Add("TARGET_ARCHITECTURE=" + Target.Architectures);
		PublicDefinitions.Add("TARGET_BUILD_ALL_MODULES=" + (Target.bBuildAllModules ? "1" : "0"));
		bool distribution = Environment.GetCommandLineArgs().Any(argument => argument.StartsWith("-plugin=", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(Path.GetFullPath(argument.Substring(8).Trim('"')), Path.Combine(PluginDirectory, "UnrealSharp.uplugin"), StringComparison.OrdinalIgnoreCase));
		PublicDefinitions.Add("UNREALSHARP_DISTRIBUTION=" + (distribution ? "1" : "0"));

		// AOT is published during staging, after the native target has been built.
		// UAT regenerates its run-only Xcode project afterwards. Only that process
		// receives this path; a first native build must not require an AOT output.
		// Xcode's framework discovery uses Development rules even for Shipping,
		// so use the publishing receipt's path rather than Target.Configuration.
		string iosFramework = Environment.GetEnvironmentVariable("UNREALSHARP_IOS_FRAMEWORK");
		if (Target.Platform == UnrealTargetPlatform.IOS && Target.ProjectFile != null &&
			!string.IsNullOrEmpty(iosFramework))
		{
			string publishRoot = Path.Combine(Target.ProjectFile.Directory.FullName,
				"Intermediate", "UnrealSharp", "Publish", Target.Name, "IOS") + Path.DirectorySeparatorChar;
			string frameworkPath = Path.GetFullPath(iosFramework);
			if (frameworkPath.StartsWith(publishRoot, StringComparison.Ordinal))
			{
				if (!Directory.Exists(frameworkPath))
				{
					throw new BuildException("Missing published UnrealSharp iOS framework: {0}", frameworkPath);
				}
				foreach (string path in Directory.GetDirectories(Path.GetDirectoryName(frameworkPath), "*.framework"))
				{
					string name = Path.GetFileNameWithoutExtension(path);
					if (!File.Exists(Path.Combine(path, name)) || !File.Exists(Path.Combine(path, "Info.plist")))
						throw new BuildException("Incomplete UnrealSharp framework: {0}", path);
					PublicAdditionalFrameworks.Add(new Framework(name, path, Framework.FrameworkMode.Copy));
				}
			}
		}
		
		PublicDependencyModuleNames.AddRange(
			new string[]
			{
				"Core", 
				"GameplayTags", 
				"UnrealSharpUtilities",
				"UnrealSharpBinds",
			}
			);
		
		PrivateDependencyModuleNames.AddRange(
			new string[]
			{
				"CoreUObject",
				"Engine",
				"Slate",
				"SlateCore", 
				"Boost", 
				"Projects",
				"UMG", 
				"DeveloperSettings", 
				"UnrealSharpUtilities", 
				"EnhancedInput", 
				"UnrealSharpUtilities",
				"GameplayTags", 
				"AIModule",
				"UnrealSharpBinds",
				"FieldNotification",
				"InputCore",
				"Json"
			});

        PublicIncludePaths.AddRange(new string[] { ModuleDirectory });
        PublicDefinitions.Add("ForceAsEngineGlue=1");
        PublicSystemIncludePaths.Add(Path.Combine(PluginDirectory, "Managed", "DotNetRuntime", "inc"));

		if (Target.Platform == UnrealTargetPlatform.Android)
		{
			AdditionalPropertiesForReceipt.Add("AndroidPlugin",
				Path.Combine(ModuleDirectory, "Private", "Platform", "Android", "UnrealSharpCore_UPL.xml"));
		}

		if (Target.bBuildEditor)
		{
			PrivateDependencyModuleNames.AddRange(new string[]
			{
				"UnrealEd", 
				"EditorSubsystem",
				"BlueprintGraph",
				"BlueprintEditorLibrary"
			});
		}
	}
}


