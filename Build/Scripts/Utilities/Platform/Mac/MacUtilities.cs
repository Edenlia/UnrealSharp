using System;
using System.Collections.Generic;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;

namespace UnrealSharp.Automation.Utilities.Platform.Mac;

internal sealed class MacUtilities : PlatformUtilities
{
    public override string DotNetPlatformIdentifier => "osx";
    public override string NativeLibraryExtension => ".dylib";
    public override UnrealArch DefaultArchitecture => UnrealArch.X64;
    public override IReadOnlyList<RuntimeFlavor> SupportedRuntimes { get; } = [RuntimeFlavor.CoreClrHostFxr, RuntimeFlavor.NativeAot];

    public override bool SupportsNativeAOT(UnrealArch architecture)
    {
        return OperatingSystem.IsMacOS() && architecture == UnrealArch.Arm64;
    }

    public override void StageManagedFiles(DeploymentContext context, DirectoryReference publishDirectory, StagedDirectoryReference stageDirectory)
    {
        context.StageFiles(StagedFileType.NonUFS, publishDirectory, "*", StageFilesSearch.AllDirectories, stageDirectory);
    }
}
