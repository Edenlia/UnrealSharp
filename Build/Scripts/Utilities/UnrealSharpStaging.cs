using System;
using System.IO;
using System.Linq;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;
using UnrealSharp.Automation.BuildCommands;
using UnrealSharp.Automation.Utilities.Platform;

namespace UnrealSharp.Automation.Utilities;

internal static class UnrealSharpStaging
{
    internal static void Stage(ProjectParams Params, DeploymentContext SC)
    {
        TargetReceipt[] Receipts = SC.StageTargets
            .Select(target => target.Receipt)
            .Where(UsesUnrealSharp)
            .ToArray();
        if (Receipts.Length == 0)
        {
            return;
        }
        if (Params.HasDLCName)
        {
            throw new AutomationException("UnrealSharp managed staging does not support DLC. Publish the managed code as part of the base game.");
        }
        TargetReceipt Receipt = Receipts[0];

        // All managed assemblies share one runtime directory. Different target
        // configurations or architectures cannot safely share that directory.
        if (Receipts.Length != 1 || SC.StageTargets.Any(target =>
                target.Receipt.TargetType is TargetType.Game or TargetType.Client or TargetType.Server &&
                target.Receipt != Receipt))
        {
            throw new AutomationException("UnrealSharp requires a single game, client or server target per staging directory. Stage different targets and configurations separately.");
        }
        PlatformUtilities Platform = PlatformUtilities.For(Receipt.Platform);
        if (Receipt.Architectures.Architectures.Count != 1)
        {
            throw new AutomationException("UnrealSharp managed staging requires a single architecture. Stage architectures separately.");
        }
        if (Receipt.ProjectFile != null && Receipt.ProjectFile != SC.RawProjectPath)
        {
            throw new AutomationException($"The UnrealSharp target receipt belongs to a different project: {Receipt.ProjectFile}");
        }

        PackageProject Command = new()
        {
            Params =
            [
                $"Project={SC.RawProjectPath.FullName}",
                $"TargetPlatform={Receipt.Platform}",
                $"UETargetType={Receipt.TargetType}",
                $"UEBuildConfig={Receipt.Configuration}"
            ]
        };
        FileReference PluginFile = Receipt.RuntimeDependencies
            .Single(dependency => dependency.Path.GetFileName().Equals("UnrealSharp.uplugin", StringComparison.OrdinalIgnoreCase)).Path;
        if (Command.GetUnrealSharpUPlugin() != PluginFile)
        {
            throw new AutomationException($"The UnrealSharp plugin selected for publishing does not match the native target receipt: {PluginFile}");
        }

        string PublishRoot = Platform.GetPublishRoot(Command, Receipt);
        Directory.CreateDirectory(PublishRoot);

        LoggerUtilities.LogUnrealSharpInfo($"Publishing managed code for staged target '{Receipt.TargetName}'.");
        RuntimeFlavor Runtime = RuntimeFlavorUtilities.Resolve(Command, Receipt.Platform, Platform,
            commandLineRuntime: null, commandLineNativeAot: false);
        Command.StartPackaging(new PackageProject.PackagingOptions(PublishRoot, Receipt.TargetType,
            Receipt.Configuration, Receipt.Platform, Receipt.Architectures.SingleArchitecture, Runtime));

        DirectoryReference PublishDirectory = new(PathUtilities.BuildOutputPath(PublishRoot));
        StagedDirectoryReference StageDirectory = StagedDirectoryReference.Combine(SC.RelativeProjectRootForStage,
            "Binaries", "Managed", PublishDirectory.GetDirectoryName());
        Platform.StageManagedFiles(SC, PublishDirectory, StageDirectory);
    }

    internal static bool UsesUnrealSharp(TargetReceipt receipt)
    {
        return receipt.TargetType is TargetType.Game or TargetType.Client or TargetType.Server &&
            receipt.RuntimeDependencies.Any(dependency =>
                dependency.Path.GetFileName().Equals("UnrealSharp.uplugin", StringComparison.OrdinalIgnoreCase));
    }
}
