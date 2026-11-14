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

		// Runtime hosts compiled into this target. Mobile platforms ship no hostfxr and do not allow a JIT.
		bool bMobile = Target.Platform == UnrealTargetPlatform.IOS || Target.Platform == UnrealTargetPlatform.Android;
		PrivateDefinitions.Add("UNREALSHARP_WITH_HOSTFXR_HOST=" + (bMobile ? "0" : "1"));
		PrivateDefinitions.Add("UNREALSHARP_WITH_NATIVEAOT_HOST=1");
		// coreclr_initialize hosting. Apple platforms load Mach-O ReadyToRun images; Android hands assemblies
		// (and any PE ReadyToRun composite) to the runtime through external_assembly_probe.
		bool bEmbeddedCoreClr = Target.Platform == UnrealTargetPlatform.IOS || Target.Platform == UnrealTargetPlatform.Mac ||
			Target.Platform == UnrealTargetPlatform.Android;
		PrivateDefinitions.Add("UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST=" + (bEmbeddedCoreClr ? "1" : "0"));

		// AOT is published during staging, after the native target has been built.
		// UAT regenerates its run-only Xcode project afterwards. Only that process
		// receives the published Frameworks directory: the NativeAOT library, or libcoreclr,
		// the runtime's native libraries and the ReadyToRun composite for CoreClrEmbedded.
		// Xcode's framework discovery uses Development rules even for Shipping,
		// so use the publishing receipt's path rather than Target.Configuration.
		string iosFrameworks = Environment.GetEnvironmentVariable("UNREALSHARP_IOS_FRAMEWORKS");
		if (Target.Platform == UnrealTargetPlatform.IOS && Target.ProjectFile != null &&
			!string.IsNullOrEmpty(iosFrameworks))
		{
			string publishRoot = Path.Combine(Target.ProjectFile.Directory.FullName,
				"Intermediate", "UnrealSharp", "Publish", Target.Name, "IOS") + Path.DirectorySeparatorChar;
			string frameworksDirectory = Path.GetFullPath(iosFrameworks);
			if (frameworksDirectory.StartsWith(publishRoot, StringComparison.Ordinal))
			{
				if (!Directory.Exists(frameworksDirectory))
				{
					throw new BuildException("Missing published UnrealSharp iOS frameworks: {0}", frameworksDirectory);
				}

				string[] frameworkPaths = Directory.GetDirectories(frameworksDirectory, "*.framework");
				Array.Sort(frameworkPaths, StringComparer.Ordinal);
				foreach (string frameworkPath in frameworkPaths)
				{
					string frameworkName = Path.GetFileNameWithoutExtension(frameworkPath);
					if (!File.Exists(Path.Combine(frameworkPath, frameworkName)) ||
						!File.Exists(Path.Combine(frameworkPath, "Info.plist")))
					{
						throw new BuildException("Incomplete published UnrealSharp iOS framework: {0}", frameworkPath);
					}
					// Embedded and signed with the app; the runtime host loads each one with dlopen.
					PublicAdditionalFrameworks.Add(new Framework(frameworkName, frameworkPath, Framework.FrameworkMode.Copy));
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


