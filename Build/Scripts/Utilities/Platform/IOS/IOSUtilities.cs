using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    // iOS forbids JIT compilation, so hostfxr-based JIT packages cannot run there. CoreCLR runs from ReadyToRun
    // code plus its interpreter, started through the embedded host.
    public override IReadOnlyList<RuntimeFlavor> SupportedRuntimes { get; } = [RuntimeFlavor.NativeAot, RuntimeFlavor.CoreClrEmbedded];

    public override bool SupportsNativeAOT(UnrealArch architecture)
    {
        return OperatingSystem.IsMacOS() && architecture == UnrealArch.Arm64;
    }

    public override bool SupportsCoreClrEmbedded(UnrealArch architecture)
    {
        return OperatingSystem.IsMacOS() && architecture == UnrealArch.Arm64;
    }

    // Apple platforms only map native code from signed Mach-O images; the SDK links the composite into a dylib.
    public override string? CoreClrEmbeddedReadyToRunContainerFormat => "macho";

    public override void ConfigureNativeAOTProject(Action<string, string> setProperty)
    {
        // Use the NativeAOT runtime pack without requiring the .NET iOS workload.
        setProperty("PublishAotUsingRuntimePack", "true");
    }

    public override string FinalizeNativeAOT(string outputDirectory, string nativeLibrary)
    {
        return MoveIntoFramework(outputDirectory, nativeLibrary, FrameworkName, "com.unrealsharp.nativeaot");
    }

    public override CoreClrEmbeddedLayout FinalizeCoreClrEmbedded(string outputDirectory)
    {
        CoreClrEmbeddedLayout Published = base.FinalizeCoreClrEmbedded(outputDirectory);

        // iOS forbids JIT code generation, and the DAC/DBI libraries only serve an attached debugger.
        foreach (string Library in Directory.GetFiles(outputDirectory, "*" + NativeLibraryExtension))
        {
            if (IsCoreClrPrivateLibrary(Path.GetFileName(Library)) && Path.GetFileName(Library) != Published.CoreClrLibrary)
            {
                File.Delete(Library);
            }
        }

        string CoreClr = MoveIntoFramework(outputDirectory, Published.CoreClrLibrary, "libcoreclr", "com.unrealsharp.coreclr");

        Dictionary<string, string> NativeLibraries = new(StringComparer.Ordinal);
        foreach ((string Name, string File) in Published.NativeLibraries)
        {
            NativeLibraries[Name] = MoveIntoFramework(outputDirectory, File, Name, "com.unrealsharp.coreclr." + ToBundleIdentifierPart(Name));
        }

        Dictionary<string, string> ReadyToRunImages = new(StringComparer.Ordinal);
        foreach ((string Composite, string File) in Published.ReadyToRunImages)
        {
            string Name = Path.GetFileNameWithoutExtension(File);
            ReadyToRunImages[Composite] = MoveIntoFramework(outputDirectory, File, Name, "com.unrealsharp.r2r." + ToBundleIdentifierPart(Name));
        }

        return new CoreClrEmbeddedLayout(CoreClr, NativeLibraries, ReadyToRunImages);
    }

    private static string ToBundleIdentifierPart(string name)
    {
        // Bundle identifiers allow letters, digits, hyphens and periods.
        return new string(name.Select(Character => char.IsAsciiLetterOrDigit(Character) || Character is '.' or '-' ? Character : '-').ToArray());
    }

    /// <summary>
    /// Moves a published dylib into Frameworks/&lt;name&gt;.framework, because App Store apps may only load dynamic
    /// libraries from embedded frameworks. Returns the reference relative to the app's Frameworks directory.
    /// </summary>
    private static string MoveIntoFramework(string outputDirectory, string dylib, string frameworkName, string bundleIdentifier)
    {
        string FrameworkDirectory = Path.Combine(outputDirectory, "Frameworks", frameworkName + ".framework");
        Directory.CreateDirectory(FrameworkDirectory);
        string FrameworkBinary = Path.Combine(FrameworkDirectory, frameworkName);
        File.Move(Path.Combine(outputDirectory, dylib), FrameworkBinary);

        // Let the final Xcode packaging step embed and sign the framework with the app's identity.
        RunXcrun("install_name_tool", "-id", $"@rpath/{frameworkName}.framework/{frameworkName}", FrameworkBinary);

        // Libraries from the .NET runtime pack carry an ad-hoc signature whose identifier is the file name. Xcode keeps
        // that identifier when it re-signs embedded frameworks, and iOS refuses to install a framework whose signing
        // identifier differs from its bundle identifier.
        RunXcrun("codesign", "--force", "--sign", "-", "--identifier", bundleIdentifier, FrameworkBinary);

        File.WriteAllText(Path.Combine(FrameworkDirectory, "Info.plist"), $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>CFBundleName</key><string>{{frameworkName}}</string>
              <key>CFBundleIdentifier</key><string>{{bundleIdentifier}}</string>
              <key>CFBundleVersion</key><string>1.0</string>
              <key>CFBundleShortVersionString</key><string>1.0</string>
              <key>CFBundleExecutable</key><string>{{frameworkName}}</string>
              <key>CFBundlePackageType</key><string>FMWK</string>
            </dict></plist>
            """);

        // Relative to the app's Frameworks directory, where Xcode embeds the framework.
        return $"{frameworkName}.framework/{frameworkName}";
    }

    private static void RunXcrun(params string[] arguments)
    {
        ProcessStartInfo StartInfo = new("/usr/bin/xcrun") { UseShellExecute = false };
        foreach (string Argument in arguments)
        {
            StartInfo.ArgumentList.Add(Argument);
        }
        using Process Process = Process.Start(StartInfo) ?? throw new AutomationException($"Could not run {arguments[0]}.");
        Process.WaitForExit();
        if (Process.ExitCode != 0) throw new AutomationException($"{arguments[0]} failed with exit code {Process.ExitCode}.");
    }

    public override void StageManagedFiles(DeploymentContext context, DirectoryReference publishDirectory, StagedDirectoryReference stageDirectory)
    {
        // Publishing happens after the native build. The run-only Xcode project
        // generated for packaging embeds and signs every published framework with the app.
        DirectoryReference Frameworks = DirectoryReference.Combine(publishDirectory, "Frameworks");
        string[] FrameworkPaths = Directory.Exists(Frameworks.FullName)
            ? Directory.GetDirectories(Frameworks.FullName, "*.framework")
            : [];
        if (FrameworkPaths.Length == 0)
        {
            throw new AutomationException($"No UnrealSharp iOS frameworks were published to: {Frameworks}");
        }
        foreach (string FrameworkPath in FrameworkPaths)
        {
            string Name = Path.GetFileNameWithoutExtension(FrameworkPath);
            if (!File.Exists(Path.Combine(FrameworkPath, Name)) || !File.Exists(Path.Combine(FrameworkPath, "Info.plist")))
            {
                throw new AutomationException($"Incomplete published UnrealSharp iOS framework: {FrameworkPath}");
            }
        }
        Environment.SetEnvironmentVariable("UNREALSHARP_IOS_FRAMEWORKS", Frameworks.FullName);
        IEnumerable<FileReference> Files = DirectoryReference.EnumerateFiles(publishDirectory, "*", SearchOption.AllDirectories)
            .Where(file => !file.IsUnderDirectory(Frameworks) && !file.FullName.Contains(".dSYM/", StringComparison.Ordinal));
        StageNonUFSFiles(context, publishDirectory, Files, stageDirectory);
    }
}
