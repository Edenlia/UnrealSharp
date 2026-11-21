using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using UnrealSharp.Automation.Processes;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.Utilities;

internal static class ManagedTrimmingUtilities
{
    private const string TrimMetadataKey = "UnrealSharp.TrimAssembly";

    private sealed record PublishedAssembly(string Path, string Name, string Action);

    internal static void Trim(string publishFolder, string intermediateFolder)
    {
        string WorkingFolder = Path.Combine(intermediateFolder, "ManagedTrim", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkingFolder);
        string LinkedFolder = Path.Combine(WorkingFolder, "linked");
        string DescriptorPath = Path.Combine(WorkingFolder, "UnrealSharp.Trimming.xml");
        string LinkAttributesPath = Path.Combine(WorkingFolder, "UnrealSharp.LinkAttributes.xml");
        string ProjectPath = Path.Combine(WorkingFolder, "UnrealSharpTrim.csproj");
        LoggerUtilities.LogUnrealSharpInfo($"Preparing managed assembly trimming in '{WorkingFolder}'.");

        IReadOnlyList<PublishedAssembly> Assemblies = PrepareInputs(publishFolder, DescriptorPath, LinkAttributesPath);
        PublishedAssembly[] Selected = Assemblies.Where(Assembly => Assembly.Action == "link").ToArray();
        if (Selected.Length == 0)
        {
            throw new InvalidOperationException($"No assemblies selected for UnrealSharp trimming in '{publishFolder}'.");
        }

        WriteLinkProject(ProjectPath, LinkedFolder, DescriptorPath, LinkAttributesPath, Assemblies);
        using DotnetProcess LinkProcess = new();
        LinkProcess.StartInfo.WorkingDirectory = WorkingFolder;
        foreach (string Argument in new[] { "build", ProjectPath, "-t:TrimPublishedAssemblies", "--nologo",
                     "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
                     "-p:ImportDirectoryPackagesProps=false" })
        {
            LinkProcess.StartInfo.ArgumentList.Add(Argument);
        }
        LinkProcess.StartProcess();

        // Keep every published assembly identity, so deps.json and load order manifests still resolve.
        foreach (PublishedAssembly Assembly in Selected)
        {
            string Output = Path.Combine(LinkedFolder, Path.GetFileName(Assembly.Path));
            if (AssemblyName.GetAssemblyName(Output).FullName != AssemblyName.GetAssemblyName(Assembly.Path).FullName)
            {
                throw new InvalidDataException($"Unexpected linked assembly identity in '{Output}'.");
            }
        }

        long Before = 0;
        long After = 0;
        foreach (PublishedAssembly Assembly in Selected)
        {
            string Output = Path.Combine(LinkedFolder, Path.GetFileName(Assembly.Path));
            long OriginalSize = new FileInfo(Assembly.Path).Length;
            long LinkedSize = new FileInfo(Output).Length;
            Before += OriginalSize;
            After += LinkedSize;
            File.Copy(Output, Assembly.Path, overwrite: true);

            string OriginalSymbols = Path.ChangeExtension(Assembly.Path, ".pdb");
            string LinkedSymbols = Path.ChangeExtension(Output, ".pdb");
            if (File.Exists(LinkedSymbols))
            {
                File.Copy(LinkedSymbols, OriginalSymbols, overwrite: true);
            }
            else
            {
                File.Delete(OriginalSymbols);
            }
            LoggerUtilities.LogUnrealSharpInfo($"Trimmed {Assembly.Name} from {OriginalSize:N0} to {LinkedSize:N0} bytes.");
        }

        LoggerUtilities.LogUnrealSharpInfo($"Trimmed {Selected.Length} managed assemblies from {Before:N0} to {After:N0} bytes.");
        Directory.Delete(WorkingFolder, recursive: true);
    }

    private static IReadOnlyList<PublishedAssembly> PrepareInputs(string publishFolder, string descriptorPath, string linkAttributesPath)
    {
        HashSet<string> FrameworkFiles = ReadRuntimePackFiles(Path.Combine(publishFolder, "UnrealSharp.Plugins.deps.json"));
        string GlueManifest = Path.Combine(publishFolder, AssemblyUtilities.MakeLoadOrderFileName(LoadOrderUtilities.GlueLoadOrderName));
        HashSet<string> GlueNames = File.Exists(GlueManifest)
            ? new(AssemblyUtilities.ReadLoadOrder(GlueManifest), StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase);

        XElement Descriptor = new("linker");
        XElement LinkAttributes = new("linker");
        Dictionary<string, PublishedAssembly> Assemblies = new(StringComparer.OrdinalIgnoreCase);
        foreach (string PathName in Directory.EnumerateFiles(publishFolder, "*.dll").Order(StringComparer.Ordinal))
        {
            using FileStream Stream = File.OpenRead(PathName);
            using PEReader Pe = new(Stream);
            if (!Pe.HasMetadata || !Pe.GetMetadataReader().IsAssembly)
            {
                continue;
            }

            MetadataReader Metadata = Pe.GetMetadataReader();
            string Name = Metadata.GetString(Metadata.GetAssemblyDefinition().Name);
            string Action = "copy";
            if (FrameworkFiles.Contains(Path.GetFileName(PathName)))
            {
                Action = "skip";
            }
            else if (ReadTrimPreference(Metadata) ?? (Name == "UnrealSharp" || GlueNames.Contains(Name)))
            {
                CorHeader Header = Pe.PEHeaders.CorHeader!;
                if (!Header.Flags.HasFlag(CorFlags.ILOnly) || Header.ManagedNativeHeaderDirectory.Size != 0)
                {
                    throw new InvalidDataException($"'{PathName}' must contain plain IL. Run UnrealSharp trimming before native precompilation.");
                }

                Action = "link";
                Descriptor.Add(UnrealSharpTrimmingRoots.CreateAssemblyDescriptor(Metadata));
            }

            if (!Assemblies.TryAdd(Name, new PublishedAssembly(PathName, Name, Action)))
            {
                throw new InvalidDataException($"Multiple published DLLs have the assembly identity '{Name}'.");
            }

            LinkAttributes.Add(UnrealSharpTrimmingRoots.CreateLinkAttributes(Metadata, Name));
        }

        foreach (string Name in AssemblyUtilities.GetLoadOrdersInFolder(publishFolder))
        {
            if (!Assemblies.ContainsKey(Name))
            {
                throw new FileNotFoundException($"A load order refers to '{Name}', which is missing from '{publishFolder}'.");
            }
        }

        Descriptor.Save(descriptorPath);
        LinkAttributes.Save(linkAttributesPath);
        return Assemblies.Values.ToArray();
    }

    private static bool? ReadTrimPreference(MetadataReader metadata)
    {
        foreach (CustomAttributeHandle Handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            if (UnrealSharpTrimmingRoots.GetAttributeName(metadata, Handle) != "System.Reflection.AssemblyMetadataAttribute")
            {
                continue;
            }

            BlobReader Arguments = metadata.GetBlobReader(metadata.GetCustomAttribute(Handle).Value);
            if (Arguments.ReadUInt16() == 1 && Arguments.ReadSerializedString() == TrimMetadataKey)
            {
                return bool.Parse(Arguments.ReadSerializedString() ?? string.Empty);
            }
        }

        return null;
    }

    private static HashSet<string> ReadRuntimePackFiles(string depsPath)
    {
        using JsonDocument Document = JsonDocument.Parse(File.ReadAllText(depsPath));
        JsonElement Root = Document.RootElement;
        string TargetName = Root.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
        JsonElement Target = Root.GetProperty("targets").GetProperty(TargetName);
        JsonElement Libraries = Root.GetProperty("libraries");
        HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty Library in Target.EnumerateObject())
        {
            if (Libraries.GetProperty(Library.Name).GetProperty("type").GetString() == "runtimepack" &&
                Library.Value.TryGetProperty("runtime", out JsonElement Runtime))
            {
                foreach (JsonProperty Asset in Runtime.EnumerateObject())
                {
                    Files.Add(Path.GetFileName(Asset.Name));
                }
            }
        }

        if (Files.Count == 0)
        {
            throw new InvalidDataException($"No self-contained .NET runtime pack found in '{depsPath}'.");
        }

        return Files;
    }

    private static void WriteLinkProject(string projectPath, string linkedFolder, string descriptorPath, string linkAttributesPath,
        IReadOnlyList<PublishedAssembly> assemblies)
    {
        // Resolve the ILLink task through the same .NET SDK that published the managed payload.
        // Invoke the task directly to keep the original runtime features, deps files and all native assets.
        XElement Project = XElement.Parse($"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{DotNetUtilities.Version}</TargetFramework>
                <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
                <EnableDefaultItems>false</EnableDefaultItems>
              </PropertyGroup>
              <Target Name="TrimPublishedAssemblies">
                <ILLink AssemblyPaths="@(_PublishedAssembly)" RootAssemblyNames="UnrealSharp.Plugins"
                        OutputDirectory="{Path.GetFileName(linkedFolder)}"
                        RootDescriptorFiles="{Path.GetFileName(descriptorPath)}" TrimMode="skip" DefaultAction="skip"
                        TreatWarningsAsErrors="true" SingleWarn="false" KeepMetadata="all"
                        OverrideRemoval="false" UnreachableBodies="false" UnusedInterfaces="false"
                        ExtraArgs="--skip-unresolved false --nowarn IL2121 --disable-opt substitutefeatureguards --disable-opt unusedtypechecks --link-attributes {Path.GetFileName(linkAttributesPath)}" />
              </Target>
            </Project>
            """);
        Project.Add(new XElement("ItemGroup", assemblies.Select(Assembly => new XElement("_PublishedAssembly",
            new XAttribute("Include", EscapeMSBuildPath(Assembly.Path)),
            new XAttribute("IsTrimmable", "true"), new XAttribute("TrimMode", Assembly.Action)))));
        Project.Save(projectPath);
    }

    private static string EscapeMSBuildPath(string path) => string.Concat(path.Select(Character =>
        "%$@();'?*".Contains(Character) ? $"%{(int)Character:X2}" : Character.ToString()));
}
