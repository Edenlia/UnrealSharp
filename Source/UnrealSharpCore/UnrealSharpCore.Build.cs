using System.IO;
using System;
using UnrealBuildTool;

public class UnrealSharpCore : ModuleRules
{
	public UnrealSharpCore(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
		
		PublicDefinitions.Add("PLUGIN_PATH=" + PluginDirectory.Replace("\\","/"));
		PublicDefinitions.Add("TARGET_TYPE=" + (int)Target.Type);
		PublicDefinitions.Add("TARGET_CONFIGURATION=" + (int)Target.Configuration);

		// The first native build precedes managed publication. Only the later
		// run-only Xcode generation receives the staged receipt's framework path.
		string IosFramework = Environment.GetEnvironmentVariable("UNREALSHARP_IOS_FRAMEWORK");
		if (Target.Platform == UnrealTargetPlatform.IOS && Target.ProjectFile != null &&
			!string.IsNullOrEmpty(IosFramework))
		{
			string PublishRoot = Path.Combine(Target.ProjectFile.Directory.FullName,
				"Intermediate", "UnrealSharp", "Publish", Target.Name, "IOS") + Path.DirectorySeparatorChar;
			string FrameworkPath = Path.GetFullPath(IosFramework);
			if (!FrameworkPath.StartsWith(PublishRoot, StringComparison.Ordinal) ||
				!File.Exists(Path.Combine(FrameworkPath, "UnrealSharpAOT")) ||
				!File.Exists(Path.Combine(FrameworkPath, "Info.plist")))
			{
				throw new BuildException("Missing published UnrealSharp iOS framework: {0}", FrameworkPath);
			}
			PublicAdditionalFrameworks.Add(new Framework("UnrealSharpAOT", FrameworkPath, Framework.FrameworkMode.Copy));
		}
		
		PublicDependencyModuleNames.AddRange(
			new string[]
			{
				"Core", 
				"GameplayTags", 
				"UnrealSharpUtilities",
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


