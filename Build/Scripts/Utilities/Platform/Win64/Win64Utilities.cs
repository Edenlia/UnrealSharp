using System;
using System.Collections.Generic;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;

namespace UnrealSharp.Automation.Utilities.Platform.Win64;

internal sealed class Win64Utilities : PlatformUtilities
{
    public override string DotNetPlatformIdentifier => "win";
    public override string NativeLibraryExtension => ".dll";
    public override UnrealArch DefaultArchitecture => UnrealArch.X64;
    public override IReadOnlyList<RuntimeFlavor> SupportedRuntimes { get; } = [RuntimeFlavor.CoreClrHostFxr, RuntimeFlavor.NativeAot];

    public override bool SupportsNativeAOT(UnrealArch architecture)
    {
        return OperatingSystem.IsWindows() && architecture == UnrealArch.X64;
    }

    public override void StageManagedFiles(DeploymentContext context, DirectoryReference publishDirectory, StagedDirectoryReference stageDirectory)
    {
        context.StageFiles(StagedFileType.NonUFS, publishDirectory, "*", StageFilesSearch.AllDirectories, stageDirectory);
    }
}
