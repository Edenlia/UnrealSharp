using System;
using System.Collections.Generic;
using System.Linq;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;
using UnrealSharp.Automation.BuildCommands;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharp.Automation.Staging;

internal static class WindowsManagedStaging
{
    internal static bool IsEnabled(ProjectParams parameters, DeploymentContext context)
    {
        return !parameters.SkipStage && !parameters.HasDLCName &&
            context.StageTargetPlatform.PlatformType == UnrealTargetPlatform.Win64 &&
            context.StageTargets.Any(target => IsRuntimeTarget(target.Receipt) && UsesUnrealSharp(target.Receipt));
    }

    internal static void Stage(ProjectParams parameters, DeploymentContext context)
    {
        if (!IsEnabled(parameters, context))
        {
            return;
        }

        // All executables in a staging directory share the same managed output directory.
        // Even a second target without UnrealSharp can collide with the selected target.
        TargetReceipt[] Receipts = context.StageTargets.Select(target => target.Receipt)
            .Where(IsRuntimeTarget)
            .DistinctBy(receipt => (receipt.TargetName, receipt.TargetType, receipt.Platform,
                receipt.Configuration, receipt.Architectures.ToString()))
            .ToArray();
        if (Receipts.Length != 1)
        {
            throw new AutomationException("UnrealSharp requires one runtime target and configuration per staging directory. Stage each target and configuration separately.");
        }

        TargetReceipt Receipt = Receipts[0];
        if (Receipt.Platform != context.StageTargetPlatform.PlatformType ||
            (Receipt.ProjectFile != null && Receipt.ProjectFile != context.RawProjectPath))
        {
            throw new AutomationException("The UnrealSharp target receipt does not match the staging project and platform.");
        }
        if (Receipt.Architectures.Architectures.Count != 1)
        {
            throw new AutomationException("UnrealSharp staging requires a single target architecture.");
        }
        DotNetSdkUtilities.GetDotNetRuntimeIdentifier(Receipt.Platform, Receipt.Architectures.SingleArchitecture);

        // PackageProject owns cleaning and publishing. Never publish into Editor outputs
        // or the staging directory, which UAT may clean after generating the manifest.
        DirectoryReference PublishRoot = DirectoryReference.Combine(context.ProjectRoot, "Intermediate", "UnrealSharp", "Stage",
            Receipt.Platform.ToString(), Receipt.TargetName, Receipt.Configuration.ToString(), Receipt.Architectures.ToString());
        DirectoryReference.CreateDirectory(PublishRoot);

        List<KeyValuePair<string, string>> Arguments =
        [
            new("ArchiveDirectory", PublishRoot.FullName),
            new("UETargetType", Receipt.TargetType.ToString()),
            new("UEBuildConfig", Receipt.Configuration.ToString()),
            new("TargetPlatform", Receipt.Platform.ToString()),
            new("TargetArchitecture", Receipt.Architectures.SingleArchitecture.ToString())
        ];
        if (!CommandUtilities.RunCommand(nameof(PackageProject), context.RawProjectPath.FullName, Arguments))
        {
            throw new AutomationException("UnrealSharp managed publishing failed during staging.");
        }

        DirectoryReference PublishDirectory = new(PathUtilities.BuildOutputPath(PublishRoot.FullName));
        StagedDirectoryReference Destination = StagedDirectoryReference.Combine(context.RelativeProjectRootForStage,
            "Binaries", "Managed", DotNetUtilities.Version);
        context.StageFiles(StagedFileType.NonUFS, PublishDirectory, StageFilesSearch.AllDirectories, Destination);
        LoggerUtilities.LogUnrealSharpInfo($"Staged managed files for {Receipt.TargetName} {Receipt.Configuration} {Receipt.Architectures} at {Destination}.");
    }

    private static bool IsRuntimeTarget(TargetReceipt receipt)
    {
        return receipt.TargetType is TargetType.Game or TargetType.Client or TargetType.Server;
    }

    private static bool UsesUnrealSharp(TargetReceipt receipt)
    {
        return receipt.RuntimeDependencies.Any(dependency => string.Equals(dependency.Path.GetFileName(),
            "UnrealSharp.uplugin", StringComparison.OrdinalIgnoreCase));
    }
}
