using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutomationTool;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.BuildCommands;

[Help("Packages the UnrealSharp managed code into an Unreal Engine packaged build.")]
[Help("ArchiveDirectory=<Path>", "REQUIRED. The base directory containing the packaged game.")]
[Help("UETargetType=<TargetType>", "REQUIRED. The Unreal Engine target type (Editor, Game, Client, Server).")]
[Help("UEBuildConfig=<Config>", "REQUIRED. The build configuration (Debug, Development, Shipping, Test).")]
[Help("TargetPlatform=<Platform>", "Optional. Target platform. Defaults to Win64.")]
[Help("TargetArchitecture=<Arch>", "Optional. Target architecture. Defaults to X64.")]
[Help("NativeAOT", "Optional flag. Enables Native AOT compilation for the user solution.")]
[Help("TrimManaged", "Optional flag. Links UnrealSharp, glue and user code for a packaged game. Also enabled by TrimManaged in UnrealSharp.Settings.json.")]
[Help("UserParams", "Optional. Additional parameters to forward to the user solution build. " + "These should be specified in the format -UserParams=\"-p:Property=Value\" -UserParams=\"--argument\".")]
public class PackageProject : BuildCommand
{
    private const string ManagedFolderName = "Managed";
    private const string BindingsProjectFolder = "UnrealSharp";
    private const int CleanupMaxAttempts = 5;
    private const int CleanupRetryDelayMs = 1000;

    public sealed record PackagingOptions(
        string ArchiveDirectory,
        TargetType TargetType,
        UnrealTargetConfiguration BuildConfiguration,
        UnrealTargetPlatform TargetPlatform,
        UnrealArch TargetArchitecture,
        bool NativeAot,
        string[]? UserParams = null,
        bool TrimManaged = false);

    public override void ExecuteBuild()
    {
        PackagingOptions Options = ParseOptionsFromCommandLine();
        StartPackaging(Options);
    }

    private void StartPackaging(PackagingOptions options)
    {
        ValidateOptions(options);
        LogOptions(options);

        DotNetSdkUtilities.CopyGlobalJson(this);

        string PublishFolder = Path.GetFullPath(PathUtilities.BuildOutputPath(options.ArchiveDirectory));
        if (options.TrimManaged)
        {
            PackageWithManagedTrimming(options, PublishFolder);
            return;
        }

        CleanBuildArtifacts(PublishFolder);
        PublishManagedCode(options, PublishFolder);
        EmitInstalledFlagFile(PublishFolder);
        LoggerUtilities.LogUnrealSharpInfo($"Packaging complete. Published files: {PublishFolder}");
    }

    private void PublishManagedCode(PackagingOptions options, string publishFolder)
    {
        string RuntimeIdentifier = DotNetSdkUtilities.GetDotNetRuntimeIdentifier(options.TargetPlatform, options.TargetArchitecture);
        IList<string> Arguments = BuildBaseArguments(RuntimeIdentifier, options, publishFolder);

        BuildBindingsSolution(Arguments, options.BuildConfiguration);
        BuildUserBindings(publishFolder, options, Arguments);
        BuildUserSolution(publishFolder, Arguments, options);
    }

    private void PackageWithManagedTrimming(PackagingOptions options, string publishFolder)
    {
        string Attempt = Guid.NewGuid().ToString("N");
        string Pending = publishFolder + ".pending-" + Attempt;
        string Previous = publishFolder + ".previous-" + Attempt;
        string Work = Path.Combine(this.GetUnrealSharpIntermediateDirectory(), "ManagedTrim", Attempt);

        try
        {
            PublishManagedCode(options, Pending);
            ManagedTrimmingUtilities.Trim(Pending, Work);
            CleanBuildArtifacts(Path.Combine(Pending, ManagedTrimmingUtilities.DescriptorFolder));
            EmitInstalledFlagFile(Pending);

            if (Directory.Exists(publishFolder))
            {
                Directory.Move(publishFolder, Previous);
            }
            try
            {
                Directory.Move(Pending, publishFolder);
            }
            catch
            {
                if (Directory.Exists(Previous))
                {
                    Directory.Move(Previous, publishFolder);
                }
                throw;
            }
            CleanBuildArtifacts(Previous);
        }
        finally
        {
            CleanBuildArtifacts(Pending);
        }

        CleanBuildArtifacts(Work);
        LoggerUtilities.LogUnrealSharpInfo($"Packaging complete. Published files: {publishFolder}");
    }

    private PackagingOptions ParseOptionsFromCommandLine()
    {
        string ArchiveDirectory = ParseRequiredStringParam("ArchiveDirectory");
        TargetType TargetType = ParseRequiredEnumParamEnum<TargetType>("UETargetType");
        UnrealTargetConfiguration TargetConfiguration = ParseRequiredEnumParamEnum<UnrealTargetConfiguration>("UEBuildConfig");

        string? PlatformString = ParseOptionalStringParam("TargetPlatform");
        UnrealTargetPlatform TargetPlatform = string.IsNullOrEmpty(PlatformString) ? UnrealTargetPlatform.Win64 : UnrealTargetPlatform.Parse(PlatformString);

        string? ArchString = ParseOptionalStringParam("TargetArchitecture");
        UnrealArch TargetArchitecture = string.IsNullOrEmpty(ArchString) ? UnrealArch.X64 : UnrealArch.Parse(ArchString);

        bool NativeAot = ParseParam("NativeAOT");

        string[] UserParams = ParseParamValues("UserParams");

        bool TrimManaged = ParseParam("TrimManaged") || (TargetType != TargetType.Editor && this.GetTrimManaged());
        return new PackagingOptions(ArchiveDirectory, TargetType, TargetConfiguration, TargetPlatform, TargetArchitecture,
            NativeAot, UserParams, TrimManaged);
    }

    private static void LogOptions(PackagingOptions options)
    {
        LoggerUtilities.LogUnrealSharpInfo("Packaging project with parameters:");
        LoggerUtilities.LogUnrealSharpInfo($"Archive Directory: {options.ArchiveDirectory}");
        LoggerUtilities.LogUnrealSharpInfo($"Target Platform: {options.TargetPlatform}");
        LoggerUtilities.LogUnrealSharpInfo($"Target Architecture: {options.TargetArchitecture}");
        LoggerUtilities.LogUnrealSharpInfo($"UE Build Configuration: {options.BuildConfiguration}");
        LoggerUtilities.LogUnrealSharpInfo($"UE Target Type: {options.TargetType}");
        LoggerUtilities.LogUnrealSharpInfo($"Native AOT: {options.NativeAot}");
        LoggerUtilities.LogUnrealSharpInfo($"Trim managed assemblies: {options.TrimManaged}");

        if (options.UserParams is { Length: > 0 })
        {
            LoggerUtilities.LogUnrealSharpInfo($"User Params: {string.Join(' ', options.UserParams)}");
        }
    }

    private void ValidateOptions(PackagingOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(options.ArchiveDirectory);

        if (!Directory.Exists(options.ArchiveDirectory))
        {
            throw new DirectoryNotFoundException($"Archive directory does not exist: {options.ArchiveDirectory}");
        }

        if (options.NativeAot)
        {
            throw new NotSupportedException("Native AOT packaging is not currently supported. This option is reserved for future use and should not be set.");
        }

        if (options.TrimManaged && options.TargetType == TargetType.Editor)
        {
            throw new NotSupportedException("Managed trimming requires a packaged non-Editor target.");
        }

        string HostFxrPath = DotNetUtilities.LatestHostFxrPath;
        if (!File.Exists(HostFxrPath))
        {
            throw new FileNotFoundException($"Could not locate hostfxr library at expected path: {HostFxrPath}. Ensure that the .NET SDK is installed and accessible.");
        }

        ValidatePlatformArchitecture(options.TargetPlatform, options.TargetArchitecture);
    }

    private static void ValidatePlatformArchitecture(UnrealTargetPlatform platform, UnrealArch architecture)
    {
        if (platform == UnrealTargetPlatform.LinuxArm64 && architecture != UnrealArch.Arm64)
        {
            throw new ArgumentException($"Platform '{platform}' requires architecture '{UnrealArch.Arm64}', " + $"but '{architecture}' was specified.");
        }
    }

    private static void CleanBuildArtifacts(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        LoggerUtilities.LogUnrealSharpInfo($"Cleaning existing output at '{folder}'.");

        for (int Attempt = 1; Attempt <= CleanupMaxAttempts; Attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                return;
            }
            catch (Exception Ex)
            {
                if (Attempt == CleanupMaxAttempts)
                {
                    throw new IOException($"Failed to clean output directory '{folder}' after {CleanupMaxAttempts} attempts. See inner exception for details.", Ex);
                }

                LoggerUtilities.LogUnrealSharpWarning($"Attempt {Attempt} to clean output directory '{folder}' failed. Retrying... Exception: {Ex.Message}");
                System.Threading.Thread.Sleep(CleanupRetryDelayMs);
            }
        }
    }

    private static IList<string> BuildBaseArguments(string runtimeIdentifier, PackagingOptions options, string publishFolder)
    {
        List<string> Arguments =
        [
            "--runtime", runtimeIdentifier,

            "-p:UseDefaultOutputPath=true",
            
            $"-p:PublishSelfContained={(options.NativeAot ? "false" : "true")}",

            $"-p:UETargetType={options.TargetType}",
            $"-p:UEBuildConfig={options.BuildConfiguration}",

            $"-p:PublishDir=\"{publishFolder}\"",
        ];
        if (options.TrimManaged)
        {
            // The linker needs the original IL from every target project.
            Arguments.Add("-p:PublishTrimmed=false");
            Arguments.Add("-p:PublishAot=false");
            Arguments.Add("-p:PublishReadyToRun=false");
            Arguments.Add("-p:PublishSingleFile=false");
            Arguments.Add("-p:UnrealSharpExportTrimDescriptors=true");
            Arguments.Add($"-p:UnrealSharpPackageReferencePath=\"{publishFolder}\"");
        }
        return Arguments;
    }

    private void BuildBindingsSolution(IList<string> arguments, UnrealTargetConfiguration buildConfig)
    {
        string BindingsPath = Path.Combine(this.GetUnrealSharpRootFolder(), ManagedFolderName, BindingsProjectFolder);
        BuildCommands.BuildSolution.RunBuild(BindingsPath, buildConfig, publish: true, arguments);
    }

    private void BuildUserSolution(string publishFolder, IList<string> buildArguments, PackagingOptions options)
    {
        ManagedBuildTarget Target = new(options.TargetPlatform, options.TargetType, options.BuildConfiguration);
        List<FileInfo> ProjectFiles = this.GetManagedProjectFiles(Target)
            .Where(file => !ProjectUtilities.IsEditorOnlyProject(file.FullName))
            .ToList();
        if (ProjectFiles.Count == 0)
        {
            LoggerUtilities.LogUnrealSharpInfo("No managed user projects found. Skipping user publish.");
            return;
        }

        string SolutionDirectory = GenerateUserSolution.PrepareSolution(this, Target, ProjectFiles,
            GenerateUserSolution.GetBuildDirectory(this, Target), "UnrealSharpUser", forceGenerate: true);

        IList<string> BuildUserSolutionArguments = buildArguments;
        if (options.UserParams is { Length: > 0 })
        {
            // Preserve the target and raw IL layout for the combined linker invocation.
            BuildUserSolutionArguments = options.TrimManaged
                ? options.UserParams.Concat(buildArguments).ToList()
                : buildArguments.Concat(options.UserParams).ToList();
        }

        BuildCommands.BuildSolution.RunBuild(SolutionDirectory, options.BuildConfiguration, publish: true, BuildUserSolutionArguments);
        
        EmitUserLoadOrder(publishFolder, ProjectFiles);
    }

    private void BuildUserBindings(string publishFolder, PackagingOptions options, IList<string> buildArguments)
    {
        if (this.IsInstalledUnrealSharpBuild())
        {
            CopyInstalledGlue(publishFolder);
            return;
        }

        LoggerUtilities.LogUnrealSharpInfo("Source build detected. Building glue from generated projects and emitting glue load order...");
        BuildUserGlue.Build(this, options.TargetType, options.BuildConfiguration, publishFolder, false, buildArguments);
    }

    private static void EmitUserLoadOrder(string publishFolder, List<FileInfo> projectFiles)
    {
        LoadOrderOptions Options = new LoadOrderOptions
        {
            Collectible = false,
            Priority = LoadOrderUtilities.UserLoadOrderPriority
        };

        LoadOrderUtilities.TryEmitLoadOrder(projectFiles.Select(file => file.FullName), publishFolder,
            LoadOrderUtilities.UserLoadOrderName, Options, requireAllAssemblies: true);
    }

    private void CopyInstalledGlue(string publishFolder)
    {
        string GlueFileName = AssemblyUtilities.MakeLoadOrderFileName(LoadOrderUtilities.GlueLoadOrderName);
        string GlueSource = PathUtilities.BuildOutputPath(this.GetProjectRootFolder());
        string GlueManifest = Path.Combine(GlueSource, GlueFileName);

        if (!File.Exists(GlueManifest))
        {
            LoggerUtilities.LogUnrealSharpWarning($"Runtime glue manifest not found at {GlueManifest}. Was the C++ project built at least once? Packaged build may be missing generated glue.");
            return;
        }

        File.Copy(GlueManifest, Path.Combine(publishFolder, GlueFileName), true);

        foreach (string AssemblyName in AssemblyUtilities.ReadLoadOrder(GlueManifest))
        {
            CopyIfExists(Path.Combine(GlueSource, AssemblyName + ".dll"), publishFolder);
            CopyIfExists(Path.Combine(GlueSource, AssemblyName + ".pdb"), publishFolder);
        }
    }

    private static void CopyIfExists(string sourceFile, string destFolder)
    {
        if (!File.Exists(sourceFile))
        {
            return;
        }

        File.Copy(sourceFile, Path.Combine(destFolder, Path.GetFileName(sourceFile)), true);
    }

    private void EmitInstalledFlagFile(string publishFolder)
    {
        string InstalledFlagFilePath = Path.Combine(publishFolder, BuildUtilities.UnrealSharpBuildFlagFileName);
        File.WriteAllText(InstalledFlagFilePath, string.Empty);
    }
}
