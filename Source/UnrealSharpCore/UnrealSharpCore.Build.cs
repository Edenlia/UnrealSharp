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

		// Runtime hosts compiled into this target. Mobile platforms ship no hostfxr and do not allow a JIT.
		bool bMobile = Target.Platform == UnrealTargetPlatform.IOS || Target.Platform == UnrealTargetPlatform.Android;
		PrivateDefinitions.Add("UNREALSHARP_WITH_HOSTFXR_HOST=" + (bMobile ? "0" : "1"));
		PrivateDefinitions.Add("UNREALSHARP_WITH_NATIVEAOT_HOST=1");
		// coreclr_initialize hosting needs a platform loader for ReadyToRun images; only Mach-O is implemented.
		bool bApple = Target.Platform == UnrealTargetPlatform.IOS || Target.Platform == UnrealTargetPlatform.Mac;
		PrivateDefinitions.Add("UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST=" + (bApple ? "1" : "0"));

		// The first native build precedes managed publication. Only the later run-only Xcode generation
		// receives the published Frameworks directory: the NativeAOT library, or libcoreclr, the runtime's
		// native libraries and the ReadyToRun composite for CoreClrEmbedded.
		string IosFrameworks = Environment.GetEnvironmentVariable("UNREALSHARP_IOS_FRAMEWORKS");
		if (Target.Platform == UnrealTargetPlatform.IOS && Target.ProjectFile != null &&
			!string.IsNullOrEmpty(IosFrameworks))
		{
			string PublishRoot = Path.Combine(Target.ProjectFile.Directory.FullName,
				"Intermediate", "UnrealSharp", "Publish", Target.Name, "IOS") + Path.DirectorySeparatorChar;
			string FrameworksDirectory = Path.GetFullPath(IosFrameworks);
			if (!FrameworksDirectory.StartsWith(PublishRoot, StringComparison.Ordinal) || !Directory.Exists(FrameworksDirectory))
			{
				throw new BuildException("Missing published UnrealSharp iOS frameworks: {0}", FrameworksDirectory);
			}

			string[] FrameworkPaths = Directory.GetDirectories(FrameworksDirectory, "*.framework");
			Array.Sort(FrameworkPaths, StringComparer.Ordinal);
			foreach (string FrameworkPath in FrameworkPaths)
			{
				string FrameworkName = Path.GetFileNameWithoutExtension(FrameworkPath);
				if (!File.Exists(Path.Combine(FrameworkPath, FrameworkName)) ||
					!File.Exists(Path.Combine(FrameworkPath, "Info.plist")))
				{
					throw new BuildException("Incomplete published UnrealSharp iOS framework: {0}", FrameworkPath);
				}
				// Embedded and signed with the app; the runtime host loads each one with dlopen.
				PublicAdditionalFrameworks.Add(new Framework(FrameworkName, FrameworkPath, Framework.FrameworkMode.Copy));
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


