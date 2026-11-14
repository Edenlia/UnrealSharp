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
[Help("ArchiveDirectory=<Path>", "REQUIRED. The project root under which Binaries/Managed will be published.")]
[Help("TargetName=<Name>", "Optional. Select the Unreal build target for managed publishing.")]
[Help("UETargetType=<TargetType>", "REQUIRED. The Unreal Engine target type (Editor, Game, Client, Server).")]
[Help("UEBuildConfig=<Config>", "REQUIRED. The build configuration (Debug, Development, Shipping, Test).")]
[Help("TargetPlatform=<Platform>", "Optional. Target platform. Defaults to Win64.")]
[Help("TargetArchitecture=<Arch>", "Optional. Target architecture. Defaults to the platform architecture.")]
[Help("Runtime=<Runtime>", "Optional. Managed runtime to publish for: CoreClrHostFxr, CoreClrEmbedded or NativeAot. Defaults to the UnrealSharp \"Runtime\" setting, then the platform default.")]
[Help("NativeAOT", "Optional flag. Same as -Runtime=NativeAot.")]
[Help("UserParams", "Optional. Additional parameters to forward to the user solution build. " + "These should be specified in the format -UserParams=\"-p:Property=Value\" -UserParams=\"--argument\".")]
[Help("TrimFramework", "Optional flag. Trims the bundled .NET framework. Also enabled by \"TrimFramework\" in UnrealSharp.Settings.json.")]
public class PackageProject : BuildCommand
{
    private const string ManagedFolderName = "Managed";
    private const string BindingsProjectFolder = "UnrealSharp";
    private const int CleanupMaxAttempts = 5;
    private const int CleanupRetryDelayMs = 1000;
    private GlueTarget _glueTarget = null!;

    public sealed record PackagingOptions(
        string ArchiveDirectory,
        TargetType TargetType,
        UnrealTargetConfiguration BuildConfiguration,
        UnrealTargetPlatform TargetPlatform,
        UnrealArch TargetArchitecture,
        RuntimeFlavor Runtime,
        string[]? UserParams = null,
        bool TrimFramework = false)
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

        _glueTarget = GlueTargetUtilities.Resolve(this);

        DotNetSdkUtilities.CopyGlobalJson(this);

        string OutputDirectory = PathUtilities.BuildOutputPath(options.ArchiveDirectory);
        string StageFolder = options.NativeAot ? "NativeAOT" : options.Runtime.ToString();
        string PublishFolder = options.UsesSecondStage
            ? Path.Combine(this.GetUnrealSharpIntermediateDirectory(), StageFolder, _glueTarget.Name, options.TargetPlatform.ToString(), options.TargetArchitecture.ToString(), options.BuildConfiguration.ToString())
            : OutputDirectory;
        CleanBuildArtifacts(PublishFolder);

        string RuntimeIdentifier = DotNetSdkUtilities.GetDotNetRuntimeIdentifier(options.TargetPlatform, options.TargetArchitecture, options.Runtime);
        IList<string> Arguments = BuildBaseArguments(RuntimeIdentifier, options, PublishFolder);
        foreach (string Argument in GlueTargetUtilities.BuildArguments(_glueTarget)) Arguments.Add(Argument);

        BuildBindingsSolution(Arguments, options.BuildConfiguration);
        // Glue and user code must compile against the just-published runtime
        // bindings, not the shared development Editor assembly.
        Arguments.Add($"-p:UnrealSharpBindingsDirectory={PublishFolder}");
        Arguments.Add($"-p:UnrealSharpUserAssemblyDirectory={PublishFolder}");
        BuildUserBindings(PublishFolder, options, Arguments);
        BuildUserSolution(PublishFolder, Arguments, options);

        if (options.TrimFramework || this.GetTrimFramework())
        {
            if (options.UsesSecondStage)
            {
                // The first stage is framework-dependent; the second stage publishes and trims its own framework.
                LoggerUtilities.LogUnrealSharpInfo($"TrimFramework does not apply to {options.Runtime}; its second stage trims the framework itself.");
            }
            else
            {
                string TrimFolder = Path.Combine(this.GetUnrealSharpIntermediateDirectory(), "FrameworkTrim");
                FrameworkTrimmingUtilities.TrimFramework(PublishFolder, TrimFolder, RuntimeIdentifier, options.BuildConfiguration);
            }
        }


        EmitInstalledFlagFile(PublishFolder);

        if (options.UsesSecondStage)
        {
            CleanBuildArtifacts(OutputDirectory);
            if (options.NativeAot)
            {
                NativeAOTUtilities.RunAOT(this, options, PublishFolder, OutputDirectory);
            }
            else
            {
                CoreClrEmbeddedUtilities.Run(this, options, PublishFolder, OutputDirectory);
            }
            EmitInstalledFlagFile(OutputDirectory);
        }
        else
        {
            RuntimeManifestWriter.Write(PublishFolder, options.Runtime);
        }

        LoggerUtilities.LogUnrealSharpInfo($"Packaging complete. Published files: {PublishFolder}");
    }

    private PackagingOptions ParseOptionsFromCommandLine()
    {
        if (ParseParam("UETargetName") || ParseOptionalStringParam("UETargetName") != null)
        {
            throw new AutomationException("PackageProject -UETargetName archive patching is no longer supported. Use Unreal's packaging pipeline to stage managed files (BuildCookRunUnrealSharp on engines without CustomStagingHandler). For direct managed publishing, use -TargetName and set -ArchiveDirectory to the project publish root.");
        }

        string ArchiveDirectory = ParseRequiredStringParam("ArchiveDirectory");
        TargetType TargetType = ParseRequiredEnumParamEnum<TargetType>("UETargetType");
        UnrealTargetConfiguration TargetConfiguration = ParseRequiredEnumParamEnum<UnrealTargetConfiguration>("UEBuildConfig");

        string? PlatformString = ParseOptionalStringParam("TargetPlatform");
        UnrealTargetPlatform TargetPlatform = string.IsNullOrEmpty(PlatformString) ? UnrealTargetPlatform.Win64 : UnrealTargetPlatform.Parse(PlatformString);

        string? TargetName = ParseOptionalStringParam("TargetName");

        PlatformUtilities Platform = PlatformUtilities.For(TargetPlatform);
        string? ArchString = ParseOptionalStringParam("TargetArchitecture");
        UnrealArchitectures Architectures = !string.IsNullOrEmpty(ArchString)
            ? UnrealArchitectures.FromString(ArchString, TargetPlatform)!
            : !string.IsNullOrEmpty(TargetName)
                ? UnrealArchitectureConfig.ForPlatform(TargetPlatform).ActiveArchitectures(this.GetUProjectFile(), TargetName)
                : new UnrealArchitectures(Platform.DefaultArchitecture);
        if (Architectures.Architectures.Count != 1)
        {
            throw new AutomationException("Managed packaging requires a single target architecture. Universal Mac archives are not supported.");
        }
        UnrealArch TargetArchitecture = Architectures.SingleArchitecture;

        RuntimeFlavor Runtime = RuntimeFlavorUtilities.Resolve(this, TargetPlatform, Platform,
            ParseOptionalStringParam("Runtime"), ParseParam("NativeAOT"));

        string[] UserParams = ParseParamValues("UserParams");

        bool TrimFramework = ParseParam("TrimFramework");

        return new PackagingOptions(ArchiveDirectory, TargetType, TargetConfiguration, TargetPlatform, TargetArchitecture, Runtime, UserParams, TrimFramework);
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
        PlatformUtilities Platform = PlatformUtilities.For(options.TargetPlatform);
        if (!Directory.Exists(options.ArchiveDirectory))
        {
            throw new DirectoryNotFoundException($"Archive directory does not exist: {options.ArchiveDirectory}");
        }
        Platform.ValidateRuntime(options.TargetPlatform, options.Runtime);
        if (options.NativeAot && (options.TargetType == TargetType.Editor || !Platform.SupportsNativeAOT(options.TargetArchitecture)))
        {
            throw new NotSupportedException("NativeAOT requires a supported non-Editor target on a compatible host OS.");
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

            $"-p:BuildingAOT={(options.NativeAot ? "true" : "false")}",

            $"-p:PublishDir=\"{publishFolder}\"",
        ];
    }

    private void BuildBindingsSolution(IList<string> arguments, UnrealTargetConfiguration buildConfig)
    {
        List<string> FrameworkArguments = arguments.Where(argument => !argument.StartsWith("-p:UnrealSharpBuildingFramework=", StringComparison.Ordinal)).ToList();
        GlueBuildUtilities.BuildFramework(_glueTarget, publish: true, FrameworkArguments);
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
            _glueTarget.IsDistributionBuild ? Path.Combine(_glueTarget.Root, "Build", "User") : GenerateUserSolution.GetBuildDirectory(this, Target), "UnrealSharpUser", forceGenerate: true);

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
        string GlueSource = _glueTarget.OutputDirectory;
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
