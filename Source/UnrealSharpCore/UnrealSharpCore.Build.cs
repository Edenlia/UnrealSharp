using System.IO;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnrealBuildTool;

public class UnrealSharpCore : ModuleRules
{
	private static readonly Dictionary<string, string> ActiveProducerContexts = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

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

		// Keep the context beside UHT's native output. The existing UHT include
		// directory is reused without adding another native include path.
		SortedDictionary<string, string> ProducerContext = new()
		{
			["TargetName"] = Target.Name,
			["TargetType"] = Target.Type.ToString(),
			["TargetTypeValue"] = ((int)Target.Type).ToString(),
			["Platform"] = Target.Platform.ToString(),
			["Architectures"] = string.Join("+", Target.Architectures.Architectures.Select(Arch => Arch.ToString()).OrderBy(Arch => Arch, StringComparer.Ordinal)),
			["Configuration"] = Target.Configuration.ToString(),
			["ConfigurationValue"] = ((int)Target.Configuration).ToString(),
			["ProjectFile"] = Target.ProjectFile?.FullName ?? "",
			["EngineDirectory"] = EngineDirectory,
			["PluginDirectory"] = PluginDirectory,
		};
		string ProducerContextJson = JsonSerializer.Serialize(ProducerContext);
		byte[] ProducerContextBytes = Encoding.UTF8.GetBytes(ProducerContextJson);
		string ProducerContextHash = Convert.ToHexString(SHA256.HashData(ProducerContextBytes));
		// Bind UHT's actual manifest to the frozen context bytes, including its
		// complete architecture set. This is identity metadata for the exporter.
		PublicDefinitions.Add("UNREALSHARP_UHT_CONTEXT_SHA256=\"" + ProducerContextHash + "\"");
		string SharedRoot = Path.Combine(PluginDirectory, "Intermediate", "UnrealSharp", "UHT", Target.Type.ToString());
		lock (ActiveProducerContexts)
		{
			if (ActiveProducerContexts.TryGetValue(SharedRoot, out var ActiveContext) && ActiveContext != ProducerContextJson)
			{
				throw new BuildException("UnrealSharp shared UHT output requires separate invocations for different target profiles.");
			}
			ActiveProducerContexts[SharedRoot] = ProducerContextJson;
		}
		GenerateHeaderFuncs.Add(("UHT", (Logger, Directory) =>
		{
			string ContextPath = Path.Combine(Directory.FullName, "UnrealSharp.TargetContext.json");
			ExternalDependencies.Add(ContextPath);
			if (!File.Exists(ContextPath) || !File.ReadAllBytes(ContextPath).SequenceEqual(ProducerContextBytes))
			{
				string Temporary = ContextPath + ".tmp." + Guid.NewGuid().ToString("N");
				try
				{
					File.WriteAllBytes(Temporary, ProducerContextBytes);
					File.Move(Temporary, ContextPath, overwrite: true);
				}
				finally
				{
					if (File.Exists(Temporary))
					{
						File.Delete(Temporary);
					}
				}
			}
		}));

		// A different target can overwrite the shared TargetType output while this
		// target's makefile is cached. Its successful producer is a graph input too.
		foreach (string Root in new[] { PluginDirectory, Target.ProjectFile?.Directory.FullName ?? "" })
		{
			if (!string.IsNullOrEmpty(Root))
			{
				ExternalDependencies.Add(Path.Combine(Root, "Intermediate", "UnrealSharp",
					"UHT", Target.Type.ToString(), "UnrealSharp.Producer.json"));
			}
		}

		// Runtime hosts compiled into this target. Mobile platforms ship no hostfxr and do not allow a JIT.
		bool bMobile = Target.Platform == UnrealTargetPlatform.IOS || Target.Platform == UnrealTargetPlatform.Android;
		bool bWasm = Target.Platform.ToString() == "Wasm";
		PrivateDefinitions.Add("UNREALSHARP_WITH_HOSTFXR_HOST=" + (bMobile || bWasm ? "0" : "1"));
		PrivateDefinitions.Add("UNREALSHARP_WITH_NATIVEAOT_HOST=1");
		// coreclr_initialize hosting. Apple platforms load Mach-O ReadyToRun images; Android hands assemblies
		// (and any PE ReadyToRun composite) to the runtime through external_assembly_probe.
		bool bEmbeddedCoreClr = Target.Platform == UnrealTargetPlatform.IOS || Target.Platform == UnrealTargetPlatform.Mac ||
			Target.Platform == UnrealTargetPlatform.Android;
		PrivateDefinitions.Add("UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST=" + (bEmbeddedCoreClr || bWasm ? "1" : "0"));
		if (bWasm)
		{
			PrivateDependencyModuleNames.Add("UnrealSharpWasmRuntime");
		}

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
        if (!bWasm)
        {
            PublicSystemIncludePaths.Add(Path.Combine(PluginDirectory, "Managed", "DotNetRuntime", "inc"));
        }

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


