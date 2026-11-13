using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using AutomationTool;
using UnrealBuildTool;
using UnrealSharp.Automation.BuildCommands;
using UnrealSharp.Automation.Processes;
using UnrealSharp.Automation.Utilities.Platform;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.Utilities;

/// <summary>
/// The packaged files a CoreClrEmbedded host loads, as library references for the runtime manifest.
/// </summary>
public sealed record CoreClrEmbeddedLayout(
    string CoreClrLibrary,
    IReadOnlyDictionary<string, string> NativeLibraries,
    IReadOnlyDictionary<string, string> ReadyToRunImages);

/// <summary>
/// Publishes managed code for the CoreClrEmbedded runtime: CoreCLR started through coreclr_initialize, with the
/// framework and UnrealSharp assemblies compiled into one platform-native ReadyToRun composite image (see
/// <see cref="ReadyToRunScope"/>). On iOS, code missing from the composite runs on the CoreCLR interpreter.
///
/// Like NativeAOT this is a second stage. The normal publish produces framework-dependent IL for the target, then a
/// generated project republishes it self-contained with the .NET 11 SDK, which is the first SDK that ships CoreCLR
/// for Apple mobile platforms.
/// </summary>
public static class CoreClrEmbeddedUtilities
{
    public const int DotNetMajorVersion = 11;
    private const string TargetFramework = "net11.0";
    private const string DotNetRootSettingName = "CoreClrEmbeddedDotNetRoot";
    private const string DotNetRootEnvironmentVariable = "UNREALSHARP_DOTNET11_ROOT";
    private const string ReadyToRunSettingName = "CoreClrEmbeddedReadyToRun";

    /// <summary>
    /// The generated engine bindings. Compiled into the composite they take about 320 MB of the image; left as IL
    /// they are about 30 MB and run on the interpreter.
    /// </summary>
    private const string EngineBindingsAssembly = "UnrealSharp";

    /// <summary>Runtime properties the embedded host computes itself; runtimeconfig.json values for them are dropped.</summary>
    private static readonly HashSet<string> HostProperties = new(StringComparer.Ordinal)
    {
        "TRUSTED_PLATFORM_ASSEMBLIES", "APP_PATHS", "APP_CONTEXT_BASE_DIRECTORY", "NATIVE_DLL_SEARCH_DIRECTORIES",
        "SYSTEM_CORELIB_DIRECTORY", "HOST_RUNTIME_CONTRACT", "UnrealSharp.UseAssemblyDependencyResolver",
    };

    public static void Validate(PackageProject.PackagingOptions options)
    {
        if (options.TargetType == TargetType.Editor)
        {
            throw new NotSupportedException("CoreClrEmbedded is a packaged-game runtime. The Editor always uses CoreClrHostFxr.");
        }
        if (!PlatformUtilities.For(options.TargetPlatform).SupportsCoreClrEmbedded(options.TargetArchitecture))
        {
            throw new NotSupportedException("CoreClrEmbedded packaging currently supports arm64 Apple targets built on macOS.");
        }
    }

    public static void Run(BuildCommand command, PackageProject.PackagingOptions options, string publishDirectory, string outputDirectory)
    {
        PlatformUtilities Platform = PlatformUtilities.For(options.TargetPlatform);
        string RuntimeIdentifier = DotNetSdkUtilities.GetDotNetRuntimeIdentifier(options.TargetPlatform, options.TargetArchitecture, options.Runtime);
        string ProjectName = command.GetProjectName() + ".CoreCLR";

        ReadyToRunScope Scope = GetReadyToRunScope(command);
        LoggerUtilities.LogUnrealSharpInfo($"CoreClrEmbedded ReadyToRun scope: {Scope}");
        string ProjectFile = CreateProject(command, Platform, ProjectName, publishDirectory, Scope);
        Publish(command, ProjectFile, RuntimeIdentifier, outputDirectory);

        foreach (string LoadOrder in Directory.GetFiles(publishDirectory, "*.LoadOrder.json"))
        {
            File.Copy(LoadOrder, Path.Combine(outputDirectory, Path.GetFileName(LoadOrder)), true);
        }

        Dictionary<string, string> Properties = ReadRuntimeConfigProperties(Path.Combine(outputDirectory, ProjectName + ".runtimeconfig.json"));
        Properties["RUNTIME_IDENTIFIER"] = RuntimeIdentifier;

        CoreClrEmbeddedLayout Layout = Platform.FinalizeCoreClrEmbedded(outputDirectory);
        if (Layout.ReadyToRunImages.Count == 0 && Platform.CoreClrEmbeddedReadyToRun && Platform.CoreClrEmbeddedReadyToRunContainerFormat != null)
        {
            throw new AutomationException($"CoreClrEmbedded publish produced no ReadyToRun composite image in '{outputDirectory}'.");
        }

        RuntimeManifestWriter.Write(outputDirectory, RuntimeFlavor.CoreClrEmbedded, Layout.CoreClrLibrary, Properties,
            Layout.NativeLibraries, Layout.ReadyToRunImages);
        LoggerUtilities.LogUnrealSharpInfo("CoreClrEmbedded packaging complete. Runtime published to: " + outputDirectory);
    }

    /// <summary>Finds the .NET 11 SDK: the "CoreClrEmbeddedDotNetRoot" setting, UNREALSHARP_DOTNET11_ROOT, then the default installation.</summary>
    public static (string Executable, string SdkPath) FindSdk(BuildCommand command)
    {
        string? Root = null;
        if (command.TryGetElement(DotNetRootSettingName, out JsonElement Setting) && Setting.ValueKind == JsonValueKind.String)
        {
            Root = Setting.GetString();
            if (!string.IsNullOrEmpty(Root) && !Path.IsPathRooted(Root))
            {
                Root = Path.GetFullPath(Path.Combine(command.GetProjectRootFolder(), Root));
            }
        }
        if (string.IsNullOrEmpty(Root))
        {
            Root = Environment.GetEnvironmentVariable(DotNetRootEnvironmentVariable);
        }

        try
        {
            return DotNetUtilities.FindSdk(DotNetMajorVersion, Root);
        }
        catch (Exception Ex) when (Ex is not AutomationException)
        {
            throw new AutomationException("{0}", $"CoreClrEmbedded packaging needs a .NET {DotNetMajorVersion} SDK. Install one, or point the " +
                $"\"{DotNetRootSettingName}\" setting or {DotNetRootEnvironmentVariable} at its installation. {Ex.Message}");
        }
    }

    /// <summary>Which assemblies are precompiled into the ReadyToRun composite.</summary>
    public enum ReadyToRunScope
    {
        /// <summary>Everything except the generated engine bindings, which stay IL and run on the interpreter.</summary>
        ExcludeEngineBindings,

        /// <summary>The framework and every UnrealSharp and user assembly.</summary>
        All,
    }

    /// <summary>Reads the "CoreClrEmbeddedReadyToRun" setting ("ExcludeEngineBindings" by default, or "All").</summary>
    public static ReadyToRunScope GetReadyToRunScope(BuildCommand command)
    {
        if (!command.TryGetElement(ReadyToRunSettingName, out JsonElement Setting) || Setting.ValueKind == JsonValueKind.Null)
        {
            return ReadyToRunScope.ExcludeEngineBindings;
        }
        if (Setting.ValueKind == JsonValueKind.String &&
            Enum.TryParse(Setting.GetString(), ignoreCase: true, out ReadyToRunScope Scope) && Enum.IsDefined(Scope))
        {
            return Scope;
        }
        throw new AutomationException($"\"{ReadyToRunSettingName}\" must be one of: {string.Join(", ", Enum.GetNames<ReadyToRunScope>())}.");
    }

    private static string CreateProject(BuildCommand command, PlatformUtilities platform, string projectName, string directory,
        ReadyToRunScope scope)
    {
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

        SetProperty("TargetFramework", TargetFramework);
        SetProperty("AssemblyName", projectName);
        SetProperty("OutputType", "Library");
        SetProperty("EnableDefaultCompileItems", "false");
        SetProperty("SelfContained", "true");
        SetProperty("UseMonoRuntime", "false");
        SetProperty("UseAppHost", "false");
        SetProperty("GenerateRuntimeConfigurationFiles", "true");
        // One composite image for the framework and every UnrealSharp assembly, in the platform's container format.
        if (platform.CoreClrEmbeddedReadyToRun)
        {
            SetProperty("PublishReadyToRun", "true");
            SetProperty("PublishReadyToRunComposite", "true");
            if (platform.CoreClrEmbeddedReadyToRunContainerFormat is { } ContainerFormat)
            {
                SetProperty("PublishReadyToRunContainerFormat", ContainerFormat);
            }
        }
        // Trim only assemblies that opt in (the framework); UnrealSharp and user code rely on reflection.
        SetProperty("PublishTrimmed", "true");
        SetProperty("TrimMode", "partial");
        SetProperty("TrimmerSingleWarn", "false");
        SetProperty("SatelliteResourceLanguages", "en");

        XmlElement ItemGroup = Document.CreateElement("ItemGroup");
        Project.AppendChild(ItemGroup);

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
                continue;
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

        // Keep NuGet runtime assets and user content that binary references do not carry.
        foreach (string FilePath in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            string RelativePath = Path.GetRelativePath(directory, FilePath);
            if (ManagedFiles.Contains(FilePath) || RelativePath.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                RelativePath.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                FilePath.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ||
                FilePath.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) ||
                FilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(FilePath) == "global.json")
            {
                continue;
            }
            XmlElement Content = Document.CreateElement("Content");
            Content.SetAttribute("Include", RelativePath);
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
                throw new FileNotFoundException($"CoreClrEmbedded load-order assembly '{Root}' was not published to '{directory}'.");
            }
            XmlElement TrimmerRootAssembly = Document.CreateElement("TrimmerRootAssembly");
            TrimmerRootAssembly.SetAttribute("Include", Root);
            ItemGroup.AppendChild(TrimmerRootAssembly);
        }

        if (scope == ReadyToRunScope.ExcludeEngineBindings)
        {
            XmlElement Exclude = Document.CreateElement("PublishReadyToRunExclude");
            Exclude.SetAttribute("Include", EngineBindingsAssembly + ".dll");
            ItemGroup.AppendChild(Exclude);
        }

        string ProjectFile = Path.Combine(directory, projectName + ".csproj");
        Document.Save(ProjectFile);

        // The plugin's global.json pins the .NET 10 SDK; this project builds with the .NET 11 SDK.
        File.WriteAllText(Path.Combine(directory, "global.json"),
            $$"""{ "sdk": { "version": "{{DotNetMajorVersion}}.0.100", "rollForward": "latestFeature" } }""");
        return ProjectFile;
    }

    private static void Publish(BuildCommand command, string projectFile, string runtimeIdentifier, string outputDirectory)
    {
        (string Executable, string SdkPath) = FindSdk(command);
        LoggerUtilities.LogUnrealSharpInfo($"Publishing {Path.GetFileName(projectFile)} for {runtimeIdentifier} with {SdkPath}.");

        using DotnetProcess PublishProcess = new DotnetProcess(Executable, SdkPath);
        foreach (string Argument in new[] { "publish", projectFile, "--configuration", "Release", "--runtime", runtimeIdentifier,
                     $"-p:PublishDir={outputDirectory}{Path.DirectorySeparatorChar}" })
        {
            PublishProcess.StartInfo.ArgumentList.Add(Argument);
        }
        PublishProcess.StartProcess();
    }

    private static Dictionary<string, string> ReadRuntimeConfigProperties(string runtimeConfigPath)
    {
        Dictionary<string, string> Properties = new(StringComparer.Ordinal);
        if (!File.Exists(runtimeConfigPath))
        {
            throw new FileNotFoundException("CoreClrEmbedded publish produced no runtimeconfig.json.", runtimeConfigPath);
        }

        using JsonDocument Document = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));
        if (!Document.RootElement.TryGetProperty("runtimeOptions", out JsonElement RuntimeOptions) ||
            !RuntimeOptions.TryGetProperty("configProperties", out JsonElement ConfigProperties))
        {
            return Properties;
        }

        foreach (JsonProperty Property in ConfigProperties.EnumerateObject())
        {
            if (HostProperties.Contains(Property.Name))
            {
                continue;
            }
            Properties[Property.Name] = Property.Value.ValueKind switch
            {
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.String => Property.Value.GetString()!,
                _ => Property.Value.GetRawText(),
            };
        }
        return Properties;
    }
}
