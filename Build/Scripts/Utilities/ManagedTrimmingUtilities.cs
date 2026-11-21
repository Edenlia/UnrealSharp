using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using UnrealSharp.Automation.Processes;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.Utilities;

internal static class ManagedTrimmingUtilities
{
    internal const string DescriptorFolder = "UnrealSharpTrimming";
    private const string HostAssembly = "UnrealSharp.Plugins";

    private const string LinkerOptions = "--skip-unresolved false --deterministic "
        + "--disable-opt unreachablebodies --disable-opt overrideremoval "
        + "--disable-opt unusedinterfaces --disable-opt unusedtypechecks --disable-opt substitutefeatureguards";

    private static readonly string[] RuntimeAssemblies =
        ["UnrealSharp", "UnrealSharp.Core", "UnrealSharp.Binds", "UnrealSharp.Log", HostAssembly];

    internal static void Trim(string publishFolder, string workFolder)
    {
        HashSet<string> FrameworkFiles = ReadFrameworkFiles(publishFolder);
        HashSet<string> EntryAssemblies = RuntimeAssemblies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string Manifest in Directory.EnumerateFiles(publishFolder, "*.LoadOrder.json"))
        {
            EntryAssemblies.UnionWith(AssemblyUtilities.ReadLoadOrder(Manifest));
        }

        Directory.CreateDirectory(workFolder);
        LoggerUtilities.LogUnrealSharpInfo($"Preparing managed linker inputs in '{workFolder}'.");
        string LinkOutput = Path.Combine(workFolder, "Linked");
        string RootsPath = Path.Combine(workFolder, "UnrealSharp.Roots.xml");
        XElement Roots = new("linker");
        XElement Inputs = new("ItemGroup");
        List<string> LinkFiles = [];
        Dictionary<string, string> Assemblies = new(StringComparer.OrdinalIgnoreCase);
        foreach (string PathName in Directory.EnumerateFiles(publishFolder, "*.dll").OrderBy(path => path, StringComparer.Ordinal))
        {
            using FileStream Stream = File.OpenRead(PathName);
            using PEReader Pe = new(Stream);
            if (!Pe.HasMetadata)
            {
                continue;
            }

            XElement Input = new("InputAssembly", new XAttribute("Include", EscapeItem(PathName)));
            Inputs.Add(Input);
            string FileName = Path.GetFileName(PathName);
            if (FrameworkFiles.Contains(FileName))
            {
                // The native host still uses the original framework and its runtime settings.
                Input.Add(new XAttribute("TrimMode", "skip"));
                continue;
            }

            XElement Assembly = ManagedTrimRoots.Read(PathName);
            string Name = (string)Assembly.Attribute("fullname")!;
            if (!Assemblies.TryAdd(Name, PathName))
            {
                throw new InvalidDataException($"Duplicate assembly identity '{Name}' in '{publishFolder}'.");
            }
            if (EntryAssemblies.Contains(Name) && Assembly.HasElements)
            {
                if (Pe.PEHeaders.CorHeader?.ManagedNativeHeaderDirectory.Size > 0)
                {
                    throw new InvalidDataException($"'{Name}' is already precompiled. Run managed trimming before ReadyToRun.");
                }

                Input.Add(new XAttribute("TrimMode", "link"));
                Roots.Add(Assembly);
                LinkFiles.Add(PathName);
            }
            else
            {
                // Retained dependencies may call into the assemblies we are trimming.
                Input.Add(new XAttribute("TrimMode", "copy"));
                Inputs.Add(new XElement("CopyRoot", new XAttribute("Include", EscapeItem(Name))));
            }
        }

        foreach (string Name in EntryAssemblies)
        {
            if (!Assemblies.TryGetValue(Name, out string? PathName)
                || !string.Equals(Path.GetFileName(PathName), Name + ".dll", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Required assembly '{Name}.dll' is missing or has a mismatched identity in '{publishFolder}'.");
            }
        }

        Roots.Save(RootsPath);
        Inputs.Add(new XElement("Descriptor", new XAttribute("Include", EscapeItem(RootsPath))));
        string Descriptors = Path.Combine(publishFolder, DescriptorFolder);
        if (Directory.Exists(Descriptors))
        {
            foreach (string Descriptor in Directory.EnumerateFiles(Descriptors, "*.xml", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                Inputs.Add(new XElement("Descriptor", new XAttribute("Include", EscapeItem(Descriptor))));
            }
        }

        string ProjectPath = Path.Combine(workFolder, "UnrealSharpLink.csproj");
        WriteLinkProject(ProjectPath, Inputs);
        using (DotnetProcess Linker = new())
        {
            Linker.StartInfo.WorkingDirectory = workFolder;
            Linker.StartInfo.ArgumentList.Add("build");
            Linker.StartInfo.ArgumentList.Add(ProjectPath);
            Linker.StartInfo.ArgumentList.Add("-p:ImportDirectoryBuildProps=false");
            Linker.StartInfo.ArgumentList.Add("-p:ImportDirectoryBuildTargets=false");
            Linker.StartInfo.ArgumentList.Add("-p:ImportDirectoryPackagesProps=false");
            Linker.StartProcess();
        }

        foreach (string Original in LinkFiles)
        {
            string Linked = Path.Combine(LinkOutput, Path.GetFileName(Original));
            if (!File.Exists(Linked))
            {
                throw new InvalidDataException($"The linker removed required assembly '{Original}'.");
            }
        }

        long Before = 0;
        long After = 0;
        foreach (string Original in LinkFiles)
        {
            string Linked = Path.Combine(LinkOutput, Path.GetFileName(Original));
            Before += new FileInfo(Original).Length;
            After += new FileInfo(Linked).Length;
            File.Copy(Linked, Original, overwrite: true);

            string OriginalSymbols = Path.ChangeExtension(Original, ".pdb");
            string LinkedSymbols = Path.ChangeExtension(Linked, ".pdb");
            if (File.Exists(LinkedSymbols))
            {
                File.Copy(LinkedSymbols, OriginalSymbols, overwrite: true);
            }
            else
            {
                File.Delete(OriginalSymbols);
            }
        }

        LoggerUtilities.LogUnrealSharpInfo($"Trimmed {LinkFiles.Count} managed assemblies from {Before:N0} to {After:N0} bytes.");
    }

    private static HashSet<string> ReadFrameworkFiles(string publishFolder)
    {
        string PathName = Path.Combine(publishFolder, HostAssembly + ".deps.json");
        using JsonDocument Document = JsonDocument.Parse(File.ReadAllText(PathName));
        JsonElement Root = Document.RootElement;
        string TargetName = Root.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
        JsonElement Target = Root.GetProperty("targets").GetProperty(TargetName);
        HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty Library in Root.GetProperty("libraries").EnumerateObject())
        {
            if (Library.Value.GetProperty("type").GetString() == "runtimepack"
                && Target.TryGetProperty(Library.Name, out JsonElement Assets)
                && Assets.TryGetProperty("runtime", out JsonElement Runtime))
            {
                Files.UnionWith(Runtime.EnumerateObject().Select(asset => Path.GetFileName(asset.Name)));
            }
        }
        if (Files.Count == 0 || !File.Exists(Path.Combine(publishFolder, "System.Private.CoreLib.dll")))
        {
            throw new InvalidDataException($"Expected a self-contained .NET runtime in '{publishFolder}'.");
        }
        return Files;
    }

    private static void WriteLinkProject(string projectPath, XElement inputs)
    {
        XElement Project = XElement.Parse(
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{DotNetUtilities.Version}</TargetFramework>
                <PublishTrimmed>true</PublishTrimmed>
                <EnableTrimAnalyzer>false</EnableTrimAnalyzer>
                <EnableDefaultItems>false</EnableDefaultItems>
              </PropertyGroup>
              <Target Name="LinkUnrealSharp" AfterTargets="Build">
                <ILLink AssemblyPaths="@(InputAssembly)" RootAssemblyNames="@(CopyRoot)"
                        RootDescriptorFiles="@(Descriptor)" OutputDirectory="$(MSBuildProjectDirectory)/Linked"
                        TrimMode="skip" DefaultAction="skip" RemoveSymbols="false"
                        TreatWarningsAsErrors="true" SingleWarn="false" ExtraArgs="{LinkerOptions}" />
              </Target>
            </Project>
            """);
        Project.Add(inputs);
        Project.Save(projectPath);
    }

    private static string EscapeItem(string value) => string.Concat(value.Select(character =>
        "%$@();'?*".Contains(character) ? $"%{(int)character:x2}" : character.ToString()));
}
