using System.Collections.Generic;
using System.IO;
using System.Xml;
using AutomationTool;
using UnrealSharp.Automation.BuildCommands;

namespace UnrealSharp.Automation.Utilities;

public static class NativeAOTUtilities
{
	public static string GetAOTIntermediateDirectory(this BuildCommand buildCommand)
	{
		return Path.Combine(buildCommand.GetUnrealSharpIntermediateDirectory(), "NativeAOT");
	}

	public static void RunAOT(PackageProject command, string publishDirectory, string outputDirectory)
	{
		CreateAOTProject(command, publishDirectory);
		PublishAOT(command, publishDirectory, outputDirectory);
		// NativeAOT still uses logical assembly names and the existing registration order.
		foreach (string Manifest in Directory.EnumerateFiles(publishDirectory, "*.LoadOrder.json"))
		{
			File.Copy(Manifest, Path.Combine(outputDirectory, Path.GetFileName(Manifest)), true);
		}
		File.Copy(Path.Combine(publishDirectory, BuildUtilities.UnrealSharpBuildFlagFileName),
			Path.Combine(outputDirectory, BuildUtilities.UnrealSharpBuildFlagFileName), true);
	}
	
	public static void CreateAOTProject(BuildCommand buildCommand, string directory)
	{
		string ProjectName = buildCommand.GetProjectName() + ".AOT";
		
		List<KeyValuePair<string, string>> Arguments = new()
		{
			new KeyValuePair<string, string>("ProjectName", ProjectName),
			new KeyValuePair<string, string>("ProjectFolder", directory)
		};

		// The emitted manifests already reflect target/plugin/editor-only filtering.
		// Include native glue even when no user assembly directly references it.
		SortedSet<string> Assemblies = new();
		foreach (string Manifest in Directory.EnumerateFiles(directory, "*.LoadOrder.json"))
		{
			Assemblies.UnionWith(UnrealSharp.Shared.AssemblyUtilities.ReadLoadOrder(Manifest));
		}
		
		foreach (string Assembly in Assemblies)
		{
			string FileName = Path.Combine(directory, Assembly + ".dll");
			if (!File.Exists(FileName))
			{
				throw new FileNotFoundException("NativeAOT manifest assembly was not published.", FileName);
			}
			Arguments.Add(new KeyValuePair<string, string>("References", FileName));
		}

		CommandUtilities.RunCommand(nameof(GenerateProject), buildCommand, Arguments);
		
		string CsprojPath = Path.Combine(directory, $"{ProjectName}.csproj");
		AddTrimmerRootAssemblies(CsprojPath, Assemblies);
		CreateInitializeClass(directory);
	}

	public static void AddTrimmerRootAssemblies(string csprojPath, IEnumerable<string> assemblies)
	{
		XmlDocument Document = new XmlDocument();
		Document.Load(csprojPath);
		
		XmlElement ItemGroup = Document.CreateElement("ItemGroup", Document.DocumentElement!.NamespaceURI);
		Document.DocumentElement.AppendChild(ItemGroup);
		
		foreach (string Assembly in assemblies)
		{
			XmlElement TrimmerRootAssembly = Document.CreateElement("TrimmerRootAssembly", Document.DocumentElement.NamespaceURI);
			TrimmerRootAssembly.SetAttribute("Include", Assembly);
			ItemGroup.AppendChild(TrimmerRootAssembly);
		}
		
		Document.Save(csprojPath);
	}

	public static void PublishAOT(PackageProject command, string publishDirectory, string outputDirectory)
	{
		IList<string>? ExtraArguments = new List<string>
		{
			"--runtime=" + DotNetSdkUtilities.GetDotNetRuntimeIdentifier(command.Options.TargetPlatform, command.Options.TargetArchitecture),
			"-p:PublishAot=true",
			"-p:NativeLib=Shared",
			"-p:BuildingAOT=true",
			$"-p:UETargetType={command.Options.TargetType}",
			$"-p:UEBuildConfig={command.Options.BuildConfiguration}",
			"-p:TrimmerSingleWarn=false",
			$"-p:PublishDir=\"{outputDirectory}\"",
		};
			
		BuildSolution.RunBuild(publishDirectory, command.Options.BuildConfiguration, true, ExtraArguments);
	}

	public static void CreateInitializeClass(string outputDirectory)
	{
	    string FilePath = Path.Combine(outputDirectory, "Main.cs");

	    string SourceCode = """
	        using System.Runtime.InteropServices;

	        namespace UnrealSharp.Plugins;

	        public static class NativeAotEntryPoint
	        {
	            [UnmanagedCallersOnly(EntryPoint = "InitializeAotRuntime")]
	            private static unsafe void InitializeAotRuntime(byte* workingDirectoryUtf8,
	                PluginsCallbacks* pluginCallbacks, 
	                nint bindsCallbacks,
	                nint managedCallbacks,
	                FCSInitializationResult* result)
	            {
	                RuntimeBootstrap.Initialize(workingDirectoryUtf8, pluginCallbacks, bindsCallbacks, managedCallbacks, result);
	            }
	        }
	        """;

	    File.WriteAllText(FilePath, SourceCode);
	}
}
