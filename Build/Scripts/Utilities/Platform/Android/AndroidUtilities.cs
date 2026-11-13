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

    public override string DotNetPlatformIdentifier => "linux-bionic";
    public override string NativeLibraryExtension => ".so";
    public override UnrealArch DefaultArchitecture => UnrealArch.Arm64;
    // UnrealSharp does not ship a CoreCLR host for Android yet; NativeAOT is the only packaged runtime.
    public override IReadOnlyList<RuntimeFlavor> SupportedRuntimes { get; } = [RuntimeFlavor.NativeAot];

    public override bool SupportsNativeAOT(UnrealArch architecture)
    {
        return architecture == UnrealArch.Arm64;
    }

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
        return NativeLibraryName;
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
        // Native libraries are packaged by UPL; metadata and content use Unreal's UFS.
        var Content = Directory.EnumerateFiles(publishDirectory.FullName, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith(".so", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".dbg", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .Select(file => new FileReference(file));
        context.StageFiles(StagedFileType.UFS, publishDirectory, Content, stageDirectory);
    }
}
