using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;

namespace UnrealSharp.Automation.Utilities.Platform.Android;

internal sealed class AndroidUtilities : PlatformUtilities
{
    private const string NativeLibraryName = "libUnrealSharp.AOT.so";
    private const string CryptoLibraryName = "libSystem.Security.Cryptography.Native.Android";

    // NativeAOT publishes for linux-bionic; CoreCLR for Android apps uses the android RID (see GetDotNetPlatformIdentifier).
    public override string DotNetPlatformIdentifier => "linux-bionic";
    public override string NativeLibraryExtension => ".so";
    public override UnrealArch DefaultArchitecture => UnrealArch.Arm64;
    // NativeAOT stays the default. CoreClrEmbedded runs CoreCLR with its JIT, started without hostfxr.
    public override IReadOnlyList<RuntimeFlavor> SupportedRuntimes { get; } = [RuntimeFlavor.NativeAot, RuntimeFlavor.CoreClrEmbedded];

    public override bool SupportsNativeAOT(UnrealArch architecture)
    {
        return architecture == UnrealArch.Arm64;
    }

    public override bool SupportsCoreClrEmbedded(UnrealArch architecture)
    {
        return architecture == UnrealArch.Arm64;
    }

    public override string GetDotNetPlatformIdentifier(RuntimeFlavor runtime)
    {
        // The android runtime pack is CoreCLR without hostfxr, and its crypto library uses the Android platform APIs.
        return runtime == RuntimeFlavor.CoreClrEmbedded ? "android" : DotNetPlatformIdentifier;
    }

    // Code is compiled on the device by the JIT.
    public override bool CoreClrEmbeddedReadyToRun => false;

    public override void ConfigureNativeAOTProject(Action<string, string> setProperty)
    {
        setProperty("PublishAotUsingRuntimePack", "true");
    }

    public override IReadOnlyDictionary<string, string> ConfigureNativeAOTPublish(IList<string> arguments)
    {
        string? NdkRoot = Environment.GetEnvironmentVariable("NDKROOT");
        if (string.IsNullOrEmpty(NdkRoot))
        {
            throw new AutomationException("Android NativeAOT requires NDKROOT from the Unreal Android SDK setup.");
        }
        string Host = OperatingSystem.IsWindows() ? "windows-x86_64" :
            OperatingSystem.IsMacOS() ? "darwin-x86_64" : "linux-x86_64";
        string Toolchain = Path.Combine(NdkRoot.Trim('"'), "toolchains", "llvm", "prebuilt", Host);
        string ToolsDirectory = Path.Combine(Toolchain, "bin");
        string Extension = OperatingSystem.IsWindows() ? ".exe" : "";
        if (!File.Exists(Path.Combine(ToolsDirectory, "clang" + Extension)) ||
            !File.Exists(Path.Combine(ToolsDirectory, "llvm-ar" + Extension)))
        {
            throw new AutomationException($"Android NativeAOT could not locate clang and llvm-ar in '{Toolchain}'.");
        }
        arguments.Add("-p:DisableUnsupportedError=true");
        arguments.Add("-p:CppCompilerAndLinker=clang");
        arguments.Add("-p:CppLibCreator=llvm-ar");
        arguments.Add($"-p:SysRoot=\"{Path.Combine(Toolchain, "sysroot")}\"");

        // Unix.targets probes tool names with `where` on Windows. Keep NDK discovery
        // local to the publish process rather than changing the automation environment.
        return new Dictionary<string, string>
        {
            ["PATH"] = ToolsDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
        };
    }

    public override string FinalizeNativeAOT(string outputDirectory, string nativeLibrary)
    {
        File.Move(Path.Combine(outputDirectory, nativeLibrary), Path.Combine(outputDirectory, NativeLibraryName), true);
        CopyNativeLibraries(outputDirectory);
        return NativeLibraryName;
    }

    public override CoreClrEmbeddedLayout FinalizeCoreClrEmbedded(string outputDirectory)
    {
        CoreClrEmbeddedLayout Published = base.FinalizeCoreClrEmbedded(outputDirectory);
        CopyNativeLibraries(outputDirectory);

        // The crypto library calls into Java; UPL packages its classes (the jar; the dex is a prebuilt copy).
        string Jar = Path.Combine(outputDirectory, CryptoLibraryName + ".jar");
        if (File.Exists(Jar))
        {
            string JavaDirectory = Path.Combine(outputDirectory, "Java");
            Directory.CreateDirectory(JavaDirectory);
            File.Move(Jar, Path.Combine(JavaDirectory, Path.GetFileName(Jar)), true);
        }
        File.Delete(Path.Combine(outputDirectory, CryptoLibraryName + ".dex"));

        // Assemblies stay in the pak; the host reads them through the Unreal file system.
        return Published with { AssemblySource = RuntimeAssemblySource.Probe };
    }

    /// <summary>Copies every published library into Native/, which UPL packages as the APK's native libraries.</summary>
    private static void CopyNativeLibraries(string outputDirectory)
    {
        // APK native libraries share one linker namespace, including NuGet runtime assets.
        string NativeDirectory = Path.Combine(outputDirectory, "Native");
        Directory.CreateDirectory(NativeDirectory);
        foreach (string Library in Directory.GetFiles(outputDirectory, "*.so", SearchOption.AllDirectories))
        {
            string Name = Path.GetFileName(Library);
            if (!Name.StartsWith("lib", StringComparison.Ordinal))
            {
                throw new AutomationException($"Android native libraries must be named lib*.so: '{Library}'.");
            }
            if (Path.GetRelativePath(NativeDirectory, Library) != Name)
            {
                // A duplicate basename must fail instead of replacing another dependency.
                File.Copy(Library, Path.Combine(NativeDirectory, Name));
            }
        }
    }

    public override string GetPublishRoot(BuildCommand command, TargetReceipt receipt)
    {
        if (receipt.Architectures.SingleArchitecture != UnrealArch.Arm64)
        {
            throw new AutomationException("UnrealSharp Android packaging supports arm64 only.");
        }
        // Must match the configuration/ABI directory consumed by Android UPL.
        return Path.Combine(command.GetUnrealSharpIntermediateDirectory(), "Android",
            receipt.Configuration.ToString(), "arm64-v8a");
    }

    public override void StageManagedFiles(DeploymentContext context, DirectoryReference publishDirectory,
        StagedDirectoryReference stageDirectory)
    {
        // Native libraries and Java classes are packaged by UPL; assemblies, metadata and content use Unreal's UFS.
        var Content = Directory.EnumerateFiles(publishDirectory.FullName, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith(".so", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".dbg", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            .Select(file => new FileReference(file));
        context.StageFiles(StagedFileType.UFS, publishDirectory, Content, stageDirectory);
    }
}
