using System;
using System.Collections.Generic;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;

namespace UnrealSharp.Automation.Utilities.Platform.Wasm;

internal sealed class WasmUtilities : PlatformUtilities
{
    public override string DotNetPlatformIdentifier => "browser";
    public override string NativeLibraryExtension => ".a";
    public override UnrealArch DefaultArchitecture => UnrealArch.Parse("wasm64");
    public override IReadOnlyList<RuntimeFlavor> SupportedRuntimes { get; } = [RuntimeFlavor.CoreClrEmbedded];

    public override bool SupportsNativeAOT(UnrealArch architecture)
    {
        return false;
    }

    public override void ValidateCoreClrEmbedded(UnrealArch architecture)
    {
        if (!architecture.ToString().Equals("wasm32", StringComparison.OrdinalIgnoreCase) &&
            !architecture.ToString().Equals("wasm64", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Wasm requires wasm32 or wasm64, but '{architecture}' was specified.");
        }

        string RuntimeIdentifier = DotNetSdkUtilities.GetDotNetRuntimeIdentifier(
            UnrealTargetPlatform.Parse("Wasm"), architecture, RuntimeFlavor.CoreClrEmbedded);
        throw new NotSupportedException($"Wasm CoreClrEmbedded packaging requires {RuntimeIdentifier} CoreCLR and browser-host archives " +
            "linked into the UE Wasm game with the same memory, function table and Emscripten settings. " +
            "The desktop libcoreclr/ReadyToRun publish cannot provide this. Build and integrate the browser runtime link assets before packaging.");
    }

    public override void StageManagedFiles(DeploymentContext context, DirectoryReference publishDirectory, StagedDirectoryReference stageDirectory)
    {
        // CoreCLR opens assemblies through the browser filesystem, outside Unreal's pak containers.
        context.StageFiles(StagedFileType.NonUFS, publishDirectory, "*", StageFilesSearch.AllDirectories, stageDirectory);
    }
}
