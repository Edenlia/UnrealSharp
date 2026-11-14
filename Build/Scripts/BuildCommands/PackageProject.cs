using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using AutomationTool;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities;
using UnrealSharp.Automation.Utilities.Platform;
using UnrealSharp.Shared;

namespace UnrealSharp.Automation.BuildCommands;

[Help("Packages the UnrealSharp managed code into an Unreal Engine packaged build.")]
[Help("ArchiveDirectory=<Path>", "REQUIRED. The base directory containing the packaged game.")]
[Help("UETargetType=<TargetType>", "REQUIRED. The Unreal Engine target type (Editor, Game, Client, Server).")]
[Help("UEBuildConfig=<Config>", "REQUIRED. The build configuration (Debug, Development, Shipping, Test).")]
[Help("TargetPlatform=<Platform>", "Optional. Target platform. Defaults to Win64.")]
[Help("TargetArchitecture=<Arch>", "Optional. Target architecture. Defaults to Arm64 for IOS/Android, otherwise X64.")]
[Help("Runtime=<Runtime>", "Optional. Managed runtime to publish for: CoreClrHostFxr, CoreClrEmbedded or NativeAot. Defaults to the UnrealSharp \"Runtime\" setting, then the platform default.")]
[Help("NativeAOT", "Optional flag. Same as -Runtime=NativeAot.")]
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
        RuntimeFlavor Runtime,
        string[]? UserParams = null)
    {
        public bool NativeAot => Runtime == RuntimeFlavor.NativeAot;

        /// <summary>
        /// Runtimes other than CoreClrHostFxr publish framework-dependent IL first, then build the final runtime
        /// image from it in a second stage.
        /// </summary>
        public bool UsesSecondStage => Runtime != RuntimeFlavor.CoreClrHostFxr;
    }

    public override void ExecuteBuild()
    {
        PackagingOptions Options = ParseOptionsFromCommandLine();
        StartPackaging(Options);
    }

    internal void StartPackaging(PackagingOptions options)
    {
        ValidateOptions(options);
        LogOptions(options);

        DotNetSdkUtilities.CopyGlobalJson(this);

        string OutputDirectory = PathUtilities.BuildOutputPath(options.ArchiveDirectory);
        string PublishDirectory = OutputDirectory;

        if (options.UsesSecondStage)
        {
            string StageFolder = options.NativeAot ? "NativeAOT" : options.Runtime.ToString();
            PublishDirectory = Path.Combine(this.GetUnrealSharpIntermediateDirectory(), StageFolder,
                options.TargetType.ToString(), options.TargetPlatform.ToString(),
                options.TargetArchitecture.ToString(), options.BuildConfiguration.ToString());
        }

        CleanBuildArtifacts(PublishDirectory);

        string RuntimeIdentifier = DotNetSdkUtilities.GetDotNetRuntimeIdentifier(options.TargetPlatform, options.TargetArchitecture, options.Runtime);
        IList<string> Arguments = BuildBaseArguments(RuntimeIdentifier, options, PublishDirectory);

        BuildBindingsSolution(Arguments, options.BuildConfiguration);
        if (options.UsesSecondStage)
        {
            // Both generated glue and user code must bind to the freshly published target.
            Arguments.Add($"-p:UnrealSharpPackageDirectory=\"{PublishDirectory}\"");
        }
        BuildUserBindings(PublishDirectory, options, Arguments);
        BuildUserSolution(PublishDirectory, Arguments, options);

        EmitInstalledFlagFile(PublishDirectory);

        if (!options.UsesSecondStage)
        {
            RuntimeManifestWriter.Write(PublishDirectory, options.Runtime);
            LoggerUtilities.LogUnrealSharpInfo("Packaging complete. Packaged assemblies published to: " + PublishDirectory);
            return;
        }

        CleanBuildArtifacts(OutputDirectory);
        if (options.NativeAot)
        {
            NativeAOTUtilities.RunAOT(this, options, PublishDirectory, OutputDirectory);
        }
        else
        {
            CoreClrEmbeddedUtilities.Run(this, options, PublishDirectory, OutputDirectory);
        }
        EmitInstalledFlagFile(OutputDirectory);
    }

    private PackagingOptions ParseOptionsFromCommandLine()
    {
        string ArchiveDirectory = ParseRequiredStringParam("ArchiveDirectory");
        TargetType TargetType = ParseRequiredEnumParamEnum<TargetType>("UETargetType");
        UnrealTargetConfiguration TargetConfiguration = ParseRequiredEnumParamEnum<UnrealTargetConfiguration>("UEBuildConfig");

        string? PlatformString = ParseOptionalStringParam("TargetPlatform");
        UnrealTargetPlatform TargetPlatform = string.IsNullOrEmpty(PlatformString) ? UnrealTargetPlatform.Win64 : UnrealTargetPlatform.Parse(PlatformString);

        PlatformUtilities Platform = PlatformUtilities.For(TargetPlatform);
        string? ArchString = ParseOptionalStringParam("TargetArchitecture");
        UnrealArch TargetArchitecture = string.IsNullOrEmpty(ArchString)
            ? Platform.DefaultArchitecture
            : UnrealArch.Parse(ArchString);

        RuntimeFlavor Runtime = RuntimeFlavorUtilities.Resolve(this, TargetPlatform, Platform,
            ParseOptionalStringParam("Runtime"), ParseParam("NativeAOT"));

        string[] UserParams = ParseParamValues("UserParams");

        return new PackagingOptions(ArchiveDirectory, TargetType, TargetConfiguration, TargetPlatform, TargetArchitecture, Runtime, UserParams);
    }

    private static void LogOptions(PackagingOptions options)
    {
        LoggerUtilities.LogUnrealSharpInfo("Packaging project with parameters:");
        LoggerUtilities.LogUnrealSharpInfo($"Archive Directory: {options.ArchiveDirectory}");
        LoggerUtilities.LogUnrealSharpInfo($"Target Platform: {options.TargetPlatform}");
        LoggerUtilities.LogUnrealSharpInfo($"Target Architecture: {options.TargetArchitecture}");
        LoggerUtilities.LogUnrealSharpInfo($"UE Build Configuration: {options.BuildConfiguration}");
        LoggerUtilities.LogUnrealSharpInfo($"UE Target Type: {options.TargetType}");
        LoggerUtilities.LogUnrealSharpInfo($"Runtime: {options.Runtime}");

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

        PlatformUtilities Platform = PlatformUtilities.For(options.TargetPlatform);
        Platform.ValidateRuntime(options.TargetPlatform, options.Runtime);
        if (options.NativeAot)
        {
            if (options.TargetType == TargetType.Editor || !Platform.SupportsNativeAOT(options.TargetArchitecture))
            {
                throw new NotSupportedException("NativeAOT requires a non-Editor Win64 x64 target on Windows, a Mac/IOS arm64 target on macOS, or an Android arm64 target. Use JIT for Editor and managed Hot Reload.");
            }
        }
        else if (options.Runtime == RuntimeFlavor.CoreClrEmbedded)
        {
            CoreClrEmbeddedUtilities.Validate(options);
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
        return
        [
            "--runtime", runtimeIdentifier,

            "-p:UseDefaultOutputPath=true",

            // The second stage brings its own runtime; only the hostfxr layout ships the runtime from this publish.
            $"-p:PublishSelfContained={(options.UsesSecondStage ? "false" : "true")}",

            $"-p:UETargetType={options.TargetType}",
            $"-p:UEBuildConfig={options.BuildConfiguration}",

            $"-p:TargetPlatform={options.TargetPlatform}",
            $"-p:TargetArchitecture={options.TargetArchitecture}",

            $"-p:BuildingAOT={(options.NativeAot ? "true" : "false")}",

            $"-p:PublishDir=\"{publishFolder}\"",
        ];
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
            BuildUserSolutionArguments = new List<string>(buildArguments);
            foreach (string UserParam in options.UserParams)
            {
                BuildUserSolutionArguments.Add(UserParam);
            }
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
