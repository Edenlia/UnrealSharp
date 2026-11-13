using System;
using System.Diagnostics;
using System.IO;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;

namespace UnrealSharp.Automation.Utilities.Platform.IOS;

internal sealed class IOSUtilities : PlatformUtilities
{
    public const string FrameworkName = "UnrealSharpAOT";

    public override string DotNetPlatformIdentifier => "ios";
    public override string NativeLibraryExtension => ".dylib";
    public override UnrealArch DefaultArchitecture => UnrealArch.Arm64;
    public override bool RequiresNativeAOT => true;

    public override bool SupportsNativeAOT(UnrealArch architecture)
    {
        return OperatingSystem.IsMacOS() && architecture == UnrealArch.Arm64;
    }

    public override void ValidatePackagingMode(bool nativeAot, bool readyToRun = false)
    {
        if (!nativeAot && !readyToRun)
        {
            throw new NotSupportedException("iOS requires NativeAOT or ReadyToRun; JIT packaging is not supported.");
        }
    }

    public override bool UseReadyToRunForStaging()
    {
        string? Runtime = Environment.GetEnvironmentVariable("UNREALSHARP_IOS_RUNTIME");
        if (!string.IsNullOrEmpty(Runtime) && Runtime != "NativeAOT" && Runtime != "ReadyToRun")
        {
            throw new AutomationException("UNREALSHARP_IOS_RUNTIME must be NativeAOT or ReadyToRun.");
        }
        return Runtime == "ReadyToRun";
    }

    public override void ValidateReadyToRun(UnrealArch architecture)
    {
        if (!OperatingSystem.IsMacOS() || architecture != UnrealArch.Arm64)
        {
            throw new NotSupportedException("Experimental ReadyToRun requires IOS arm64 on macOS.");
        }
        IOSReadyToRunUtilities.ValidateSdk();
    }

    public override void PublishReadyToRun(string inputDirectory, string outputDirectory)
    {
        IOSReadyToRunUtilities.Publish(inputDirectory, outputDirectory);
    }

    public override void ConfigureNativeAOTProject(Action<string, string> setProperty)
    {
        // Use the NativeAOT runtime pack without requiring the .NET iOS workload.
        setProperty("PublishAotUsingRuntimePack", "true");
    }

    public override string FinalizeNativeAOT(string outputDirectory, string nativeLibrary)
    {
        string FrameworkDirectory = Path.Combine(outputDirectory, "Frameworks", FrameworkName + ".framework");
        Directory.CreateDirectory(FrameworkDirectory);
        string FrameworkBinary = Path.Combine(FrameworkDirectory, FrameworkName);
        File.Move(Path.Combine(outputDirectory, nativeLibrary), FrameworkBinary);

        // Let the final Xcode packaging step embed and sign the framework with the app's identity.
        ProcessStartInfo StartInfo = new("/usr/bin/xcrun") { UseShellExecute = false };
        foreach (string Argument in new[] { "install_name_tool", "-id",
                     $"@rpath/{FrameworkName}.framework/{FrameworkName}", FrameworkBinary })
        {
            StartInfo.ArgumentList.Add(Argument);
        }
        using Process Process = Process.Start(StartInfo) ?? throw new AutomationException("Could not run install_name_tool.");
        Process.WaitForExit();
        if (Process.ExitCode != 0) throw new AutomationException($"install_name_tool failed with exit code {Process.ExitCode}.");

        File.WriteAllText(Path.Combine(FrameworkDirectory, "Info.plist"), $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>CFBundleName</key><string>{{FrameworkName}}</string>
              <key>CFBundleIdentifier</key><string>com.unrealsharp.nativeaot</string>
              <key>CFBundleVersion</key><string>1.0</string>
              <key>CFBundleShortVersionString</key><string>1.0</string>
              <key>CFBundleExecutable</key><string>{{FrameworkName}}</string>
              <key>CFBundlePackageType</key><string>FMWK</string>
            </dict></plist>
            """);

        return $"Frameworks/{FrameworkName}.framework/{FrameworkName}";
    }

    public override void StageManagedFiles(DeploymentContext context, DirectoryReference publishDirectory, StagedDirectoryReference stageDirectory)
    {
        bool ReadyToRun = File.Exists(Path.Combine(publishDirectory.FullName, "UnrealSharp.ReadyToRun"));
        // The subsequent run-only Xcode generation discovers this framework through
        // UnrealSharpCore.Build.cs. UE's Embed Frameworks phase copies and signs it.
        // Do not declare it during the earlier native PostBuildSync: on a first build
        // it does not exist yet. Pass the receipt-specific path (Xcode discovers
        // module frameworks with Development rules, regardless of package config).
        DirectoryReference Frameworks = DirectoryReference.Combine(publishDirectory, "Frameworks");
        string FrameworkPath = Path.Combine(Frameworks.FullName, (ReadyToRun ? IOSReadyToRunUtilities.FrameworkName : FrameworkName) + ".framework");
        if (!File.Exists(Path.Combine(FrameworkPath, ReadyToRun ? IOSReadyToRunUtilities.FrameworkName : FrameworkName)) ||
            !File.Exists(Path.Combine(FrameworkPath, "Info.plist")))
        {
            throw new AutomationException($"Missing published UnrealSharp iOS framework: {FrameworkPath}");
        }
        Environment.SetEnvironmentVariable("UNREALSHARP_IOS_FRAMEWORK", FrameworkPath);
        foreach (FileReference File in DirectoryReference.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories))
        {
            if (File.IsUnderDirectory(Frameworks) || File.FullName.Contains(".dSYM/", StringComparison.Ordinal)) continue;
            if (ReadyToRun && File.HasExtension(".dll"))
            {
                // CoreCLR's CoreLib lookup and component image names are case-sensitive.
                // SystemNonUFS skips UE's lowercasing/remap; explicitly select the
                // cookeddata directory that the normal iOS copy phase already owns.
                var Destination = new StagedFileReference(stageDirectory.Name.ToLowerInvariant() + "/" + File.MakeRelativeTo(publishDirectory));
                context.StageFile(StagedFileType.SystemNonUFS, File, context.StageTargetPlatform.Remap(Destination));
                continue;
            }
            context.StageFile(StagedFileType.NonUFS, File,
                StagedFileReference.Combine(stageDirectory, File.MakeRelativeTo(publishDirectory)));
        }
    }
}
