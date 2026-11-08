using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using AutomationTool;
using UnrealBuildTool;
using UnrealSharp.Automation.BuildCommands;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.Utilities;

public static class NativeAOTUtilities
{
	public static void RunAOT(BuildCommand command, PackageProject.PackagingOptions options, string publishDirectory, string outputDirectory)
	{
		CreateAOTProject(command, publishDirectory);
		PublishAOT(options, publishDirectory, outputDirectory);
		string Extension = options.TargetPlatform == UnrealTargetPlatform.Mac ? ".dylib" : ".dll";
		string NativeLibrary = command.GetProjectName() + ".AOT" + Extension;
		if (!File.Exists(Path.Combine(outputDirectory, NativeLibrary)))
		{
			throw new FileNotFoundException($"NativeAOT publish did not produce '{NativeLibrary}' in '{outputDirectory}'.");
		}

		foreach (string Manifest in Directory.GetFiles(publishDirectory, "*.LoadOrder.json"))
		{
			File.Copy(Manifest, Path.Combine(outputDirectory, Path.GetFileName(Manifest)), true);
		}
		File.WriteAllText(Path.Combine(outputDirectory, "UnrealSharp.NativeAOT"), NativeLibrary);
		LoggerUtilities.LogUnrealSharpInfo("NativeAOT packaging complete. Native library published to: " + outputDirectory);
	}

	private static void CreateAOTProject(BuildCommand buildCommand, string directory)
	{
		string ProjectName = buildCommand.GetProjectName() + ".AOT";

		XmlDocument Document = new XmlDocument();
		XmlElement Project = Document.CreateElement("Project");
		Project.SetAttribute("Sdk", "Microsoft.NET.Sdk");
		Document.AppendChild(Project);
		XmlElement Properties = Document.CreateElement("PropertyGroup");
		Project.AppendChild(Properties);

		void SetProperty(string Name, string Value)
		{
			XmlElement Property = Document.CreateElement(Name);
			Property.InnerText = Value;
			Properties.AppendChild(Property);
		}

		SetProperty("TargetFramework", "net10.0");
		SetProperty("AssemblyName", ProjectName);
		SetProperty("OutputType", "Library");
		SetProperty("ImplicitUsings", "enable");
		SetProperty("AllowUnsafeBlocks", "true");
		SetProperty("PublishAot", "true");
		SetProperty("NativeLib", "Shared");
		SetProperty("TrimmerSingleWarn", "false");
		SetProperty("EnableDefaultCompileItems", "false");

		XmlElement ItemGroup = Document.CreateElement("ItemGroup");
		Project.AppendChild(ItemGroup);
		XmlElement Compile = Document.CreateElement("Compile");
		Compile.SetAttribute("Include", "Main.cs");
		ItemGroup.AppendChild(Compile);

		// Reference the just-published target assemblies, never the Editor binaries from Shared.props.
		HashSet<string> AssemblyNames = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> ManagedFiles = new(StringComparer.OrdinalIgnoreCase);
		foreach (string AssemblyPath in Directory.GetFiles(directory, "*.dll").OrderBy(Path.GetFileName, StringComparer.Ordinal))
		{
			string AssemblyName;
			try
			{
				AssemblyName = System.Reflection.AssemblyName.GetAssemblyName(AssemblyPath).Name!;
			}
			catch (BadImageFormatException)
			{
				continue; // Native dependencies are not managed references.
			}
			AssemblyNames.Add(AssemblyName);
			ManagedFiles.Add(AssemblyPath);
			XmlElement Reference = Document.CreateElement("Reference");
			Reference.SetAttribute("Include", AssemblyName);
			XmlElement HintPath = Document.CreateElement("HintPath");
			HintPath.InnerText = Path.GetFileName(AssemblyPath);
			Reference.AppendChild(HintPath);
			ItemGroup.AppendChild(Reference);
		}

		// Binary references do not carry NuGet runtime assets or a user's published content.
		// Preserve those files from the target publish; managed IL is embedded in the AOT image.
		foreach (string FilePath in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
		{
			if (ManagedFiles.Contains(FilePath) || FilePath.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
			    FilePath.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			XmlElement Content = Document.CreateElement("Content");
			Content.SetAttribute("Include", Path.GetRelativePath(directory, FilePath));
			Content.SetAttribute("CopyToPublishDirectory", "PreserveNewest");
			ItemGroup.AppendChild(Content);
		}

		HashSet<string> Roots = new(AssemblyUtilities.GetLoadOrdersInFolder(directory), StringComparer.OrdinalIgnoreCase)
		{
			"UnrealSharp", "UnrealSharp.Core", "UnrealSharp.Plugins"
		};
		foreach (string Root in Roots.OrderBy(Name => Name, StringComparer.Ordinal))
		{
			if (!AssemblyNames.Contains(Root))
			{
				throw new FileNotFoundException($"NativeAOT load-order assembly '{Root}' was not published to '{directory}'.");
			}
			XmlElement TrimmerRootAssembly = Document.CreateElement("TrimmerRootAssembly");
			TrimmerRootAssembly.SetAttribute("Include", Root);
			ItemGroup.AppendChild(TrimmerRootAssembly);
		}
		Document.Save(Path.Combine(directory, $"{ProjectName}.csproj"));
		CreateInitializeClass(directory);
	}

	private static void PublishAOT(PackageProject.PackagingOptions options, string publishDirectory, string outputDirectory)
	{
		IList<string> ExtraArguments = new List<string>
		{
			"--runtime", DotNetSdkUtilities.GetDotNetRuntimeIdentifier(options.TargetPlatform, options.TargetArchitecture),
			$"-p:PublishDir=\"{outputDirectory}\"",
		};

		BuildSolution.RunBuild(publishDirectory, options.BuildConfiguration, true, ExtraArguments);
	}

	private static void CreateInitializeClass(string outputDirectory)
	{
	    string FilePath = Path.Combine(outputDirectory, "Main.cs");

	    string SourceCode = """
	        using System.Runtime.InteropServices;
	        using UnrealSharp.Plugins;

	        namespace UnrealSharp.NativeAot;

	        public static class Main
	        {
	            [UnmanagedCallersOnly(EntryPoint = "InitializeAotRuntime")]
	            private static unsafe void InitializeAotRuntime(byte* workingDirectoryUtf8,
	                PluginsCallbacks* pluginCallbacks,
	                nint bindsCallbacks,
	                nint managedCallbacks,
	                FCSInitializationResult* result)
	                => UnrealSharp.Plugins.Main.InitializeRuntime(workingDirectoryUtf8, pluginCallbacks, bindsCallbacks, managedCallbacks, result);
	        }
	        """;

	    File.WriteAllText(FilePath, SourceCode);
	}
}
