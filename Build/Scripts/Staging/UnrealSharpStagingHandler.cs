using System;
using System.Collections.Generic;
using System.Linq;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;
using UnrealSharp.Automation.BuildCommands;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharp.Automation.Staging;

public sealed class UnrealSharpStagingHandler : CustomStagingHandler
{
    protected override bool TryInitialize(ProjectParams Params, DeploymentContext SC)
    {
        return !Params.SkipStage && !Params.HasDLCName && SC.StageTargets.Any(target => UsesUnrealSharp(target.Receipt));
    }

    public override void GetFilesToStage(ProjectParams Params, DeploymentContext SC)
    {
        TargetReceipt[] Receipts = SC.StageTargets.Select(target => target.Receipt)
            .Where(UsesUnrealSharp)
            .DistinctBy(receipt => (receipt.TargetName, receipt.TargetType, receipt.Platform,
                receipt.Configuration, receipt.Architectures.ToString()))
            .ToArray();
        if (Receipts.Length != 1)
        {
            throw new AutomationException("UnrealSharp staging requires a single build target and configuration per staging directory. Stage each target and configuration separately.");
        }

        TargetReceipt Receipt = Receipts[0];
        if (Receipt.Platform != SC.StageTargetPlatform.PlatformType ||
            (Receipt.ProjectFile != null && Receipt.ProjectFile != SC.RawProjectPath))
        {
            throw new AutomationException("The UnrealSharp target receipt does not match the staging project and platform.");
        }

        if (Receipt.Platform != UnrealTargetPlatform.Mac && Receipt.Platform != UnrealTargetPlatform.Win64)
        {
            throw new AutomationException($"UnrealSharp staging supports Mac and Win64, not '{Receipt.Platform}'.");
        }

        if (Receipt.Architectures.Architectures.Count != 1)
        {
            throw new AutomationException("UnrealSharp staging requires a single target architecture. Universal builds are not supported.");
        }

        // Validate the RID before publishing or touching an existing output directory.
        DotNetSdkUtilities.GetDotNetRuntimeIdentifier(Receipt.Platform, Receipt.Architectures.SingleArchitecture);

        // Publishing must precede platform app assembly/signing and must not clean Editor binaries.
        DirectoryReference PublishRoot = DirectoryReference.Combine(SC.ProjectRoot, "Intermediate", "UnrealSharp", "Stage",
            Receipt.Platform.ToString(), Receipt.TargetName, Receipt.Configuration.ToString(), Receipt.Architectures.ToString());
        DirectoryReference.CreateDirectory(PublishRoot);

        List<KeyValuePair<string, string>> Arguments =
        [
            new("ArchiveDirectory", PublishRoot.FullName),
            new("TargetName", Receipt.TargetName),
            new("UETargetType", Receipt.TargetType.ToString()),
            new("UEBuildConfig", Receipt.Configuration.ToString()),
            new("TargetPlatform", Receipt.Platform.ToString()),
            new("TargetArchitecture", Receipt.Architectures.SingleArchitecture.ToString())
        ];
        if (!CommandUtilities.RunCommand(nameof(PackageProject), SC.RawProjectPath.FullName, Arguments))
        {
            throw new AutomationException("UnrealSharp managed publishing failed during staging.");
        }

        DirectoryReference PublishDirectory = new(PathUtilities.BuildOutputPath(PublishRoot.FullName));
        StagedDirectoryReference Destination = StagedDirectoryReference.Combine(SC.RelativeProjectRootForStage,
            "Binaries", "Managed", DotNetUtilities.Version);
        SC.StageFiles(StagedFileType.NonUFS, PublishDirectory, StageFilesSearch.AllDirectories, Destination);
    }

    private static bool UsesUnrealSharp(TargetReceipt receipt)
    {
        return receipt.TargetType is TargetType.Game or TargetType.Client or TargetType.Server &&
            receipt.RuntimeDependencies.Any(dependency => string.Equals(dependency.Path.GetFileName(),
                "UnrealSharp.uplugin", StringComparison.OrdinalIgnoreCase));
    }
}
