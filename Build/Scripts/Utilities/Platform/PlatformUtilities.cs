using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities.Platform.Android;
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
        if (platform == UnrealTargetPlatform.Android)
        {
            return new AndroidUtilities();
        }
        throw new NotSupportedException($"UnrealSharp packaging does not support platform '{platform}'. Supported platforms: Win64, Mac, IOS, Android.");
    }

    public abstract string DotNetPlatformIdentifier { get; }
    public abstract string NativeLibraryExtension { get; }
    public abstract UnrealArch DefaultArchitecture { get; }

    /// <summary>Runtimes this platform can package, the default first.</summary>
    public abstract IReadOnlyList<RuntimeFlavor> SupportedRuntimes { get; }
    public RuntimeFlavor DefaultRuntime => SupportedRuntimes[0];

    public abstract bool SupportsNativeAOT(UnrealArch architecture);

    public void ValidateRuntime(UnrealTargetPlatform platform, RuntimeFlavor runtime)
    {
        if (!SupportedRuntimes.Contains(runtime))
        {
            throw new NotSupportedException($"{platform} cannot package the {runtime} runtime. Supported runtimes: {string.Join(", ", SupportedRuntimes)}.");
        }
    }

    public virtual void ConfigureNativeAOTProject(Action<string, string> setProperty)
    {
    }

    public virtual IReadOnlyDictionary<string, string>? ConfigureNativeAOTPublish(IList<string> arguments)
    {
        return null;
    }

    /// <summary>
    /// Arranges the published NativeAOT library for this platform and returns the reference the native plugin
    /// resolves at runtime (see PlatformUtilities::ResolveBundledLibraryPath in UnrealSharpUtilities).
    /// </summary>
    public virtual string FinalizeNativeAOT(string outputDirectory, string nativeLibrary)
    {
        return nativeLibrary;
    }

    /// <summary>
    /// Arranges a CoreClrEmbedded publish for this platform and reports what the host must load. The default keeps
    /// the desktop layout: libcoreclr, the runtime's native libraries and the ReadyToRun composite stay next to the
    /// managed assemblies, referenced by file name.
    /// </summary>
    public virtual CoreClrEmbeddedLayout FinalizeCoreClrEmbedded(string outputDirectory)
    {
        string CoreClr = "libcoreclr" + NativeLibraryExtension;
        if (!File.Exists(Path.Combine(outputDirectory, CoreClr)))
        {
            throw new AutomationException($"CoreClrEmbedded publish did not produce '{CoreClr}' in '{outputDirectory}'.");
        }

        Dictionary<string, string> NativeLibraries = new(StringComparer.Ordinal);
        Dictionary<string, string> ReadyToRunImages = new(StringComparer.Ordinal);
        foreach (string Library in Directory.GetFiles(outputDirectory, "*" + NativeLibraryExtension))
        {
            string FileName = Path.GetFileName(Library);
            if (IsHostingLibrary(FileName))
            {
                // Desktop self-contained publishes carry hostfxr; the embedded host never loads it.
                File.Delete(Library);
            }
            else if (FileName.EndsWith(".r2r" + NativeLibraryExtension, StringComparison.Ordinal))
            {
                ReadyToRunImages[FileName] = FileName;
            }
            else if (!IsCoreClrPrivateLibrary(FileName))
            {
                NativeLibraries[Path.GetFileNameWithoutExtension(FileName)] = FileName;
            }
        }

        // Static archives are only useful when linking the runtime into the executable.
        foreach (string Archive in Directory.GetFiles(outputDirectory, "*.a"))
        {
            File.Delete(Archive);
        }

        return new CoreClrEmbeddedLayout(CoreClr, NativeLibraries, ReadyToRunImages);
    }

    /// <summary>Libraries CoreCLR loads itself (or that only debuggers need); p/invokes never bind to them.</summary>
    protected static bool IsCoreClrPrivateLibrary(string fileName)
    {
        string Name = Path.GetFileNameWithoutExtension(fileName);
        return Name is "libcoreclr" or "libclrjit" or "libmscordaccore" or "libmscordbi" or "libclrgc" or "libclrgcexp";
    }

    /// <summary>hostfxr and hostpolicy start CoreCLR for the hostfxr layout only.</summary>
    private static bool IsHostingLibrary(string fileName)
    {
        string Name = Path.GetFileNameWithoutExtension(fileName);
        return Name is "libhostfxr" or "libhostpolicy" or "hostfxr" or "hostpolicy";
    }

    public virtual string GetPublishRoot(BuildCommand command, TargetReceipt receipt)
    {
        return Path.Combine(command.GetUnrealSharpIntermediateDirectory(), "Publish",
            receipt.TargetName, receipt.Platform.ToString(), receipt.Configuration.ToString(), receipt.Architectures.SingleArchitecture.ToString());
    }

    public abstract void StageManagedFiles(DeploymentContext context, DirectoryReference publishDirectory, StagedDirectoryReference stageDirectory);
}
