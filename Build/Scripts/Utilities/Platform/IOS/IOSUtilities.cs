using System;
using System.Collections.Generic;
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
    // iOS forbids JIT compilation, so hostfxr-based JIT packages cannot run there.
    public override IReadOnlyList<RuntimeFlavor> SupportedRuntimes { get; } = [RuntimeFlavor.NativeAot];

    public override bool SupportsNativeAOT(UnrealArch architecture)
    {
        return OperatingSystem.IsMacOS() && architecture == UnrealArch.Arm64;
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
        // Publishing happens after the native build. The run-only Xcode project
        // generated for packaging embeds and signs the framework with the app.
        DirectoryReference Frameworks = DirectoryReference.Combine(publishDirectory, "Frameworks");
        string FrameworkPath = Path.Combine(Frameworks.FullName, FrameworkName + ".framework");
        if (!File.Exists(Path.Combine(FrameworkPath, FrameworkName)) ||
            !File.Exists(Path.Combine(FrameworkPath, "Info.plist")))
        {
            throw new AutomationException($"Missing published UnrealSharp iOS framework: {FrameworkPath}");
        }
        Environment.SetEnvironmentVariable("UNREALSHARP_IOS_FRAMEWORK", FrameworkPath);
        foreach (FileReference File in DirectoryReference.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories))
        {
            if (File.IsUnderDirectory(Frameworks) || File.FullName.Contains(".dSYM/", StringComparison.Ordinal))
            {
                continue;
            }
            context.StageFile(StagedFileType.NonUFS, File,
                StagedFileReference.Combine(stageDirectory, File.MakeRelativeTo(publishDirectory)));
        }
    }
}
