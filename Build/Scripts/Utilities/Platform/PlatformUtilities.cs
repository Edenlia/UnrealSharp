using System;
using System.Collections.Generic;
using System.IO;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities.Platform.IOS;
using UnrealSharp.Automation.Utilities.Platform.Mac;
using UnrealSharp.Automation.Utilities.Platform.Win64;

namespace UnrealSharp.Automation.Utilities.Platform;

internal abstract class PlatformUtilities
{
    public static PlatformUtilities For(UnrealTargetPlatform platform)
    {
        if (platform == UnrealTargetPlatform.Win64)
        {
            return new Win64Utilities();
        }
        if (platform == UnrealTargetPlatform.Mac)
        {
            return new MacUtilities();
        }
        if (platform == UnrealTargetPlatform.IOS)
        {
            return new IOSUtilities();
        }
        throw new NotSupportedException($"UnrealSharp packaging does not support platform '{platform}'. Supported platforms: Win64, Mac, IOS.");
    }

    public abstract string DotNetPlatformIdentifier { get; }
    public abstract string NativeLibraryExtension { get; }
    public abstract UnrealArch DefaultArchitecture { get; }
    public abstract bool RequiresNativeAOT { get; }
    public abstract bool SupportsNativeAOT(UnrealArch architecture);

    public virtual void ValidatePackagingMode(bool nativeAot)
    {
    }

    public virtual void ConfigureNativeAOTProject(Action<string, string> setProperty)
    {
    }

    public virtual IReadOnlyDictionary<string, string>? ConfigureNativeAOTPublish(IList<string> arguments)
    {
        return null;
    }

    public virtual string FinalizeNativeAOT(string outputDirectory, string nativeLibrary)
    {
        return nativeLibrary;
    }

    public virtual string GetPublishRoot(BuildCommand command, TargetReceipt receipt)
    {
        return Path.Combine(command.GetUnrealSharpIntermediateDirectory(), "Publish",
            receipt.TargetName, receipt.Platform.ToString(), receipt.Configuration.ToString(), receipt.Architectures.SingleArchitecture.ToString());
    }

    public abstract void StageManagedFiles(DeploymentContext context, DirectoryReference publishDirectory, StagedDirectoryReference stageDirectory);
}
