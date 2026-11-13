using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using AutomationTool;

namespace UnrealSharp.Automation.Utilities.Platform.IOS;

internal static class IOSReadyToRunUtilities
{
    internal const string FrameworkName = "UnrealSharpR2R";

    internal static string ValidateSdk()
    {
        string? sdk = Environment.GetEnvironmentVariable("UNREALSHARP_R2R_DOTNET");
        if (string.IsNullOrEmpty(sdk) || !Path.IsPathFullyQualified(sdk) || !File.Exists(sdk))
            throw new AutomationException("Set UNREALSHARP_R2R_DOTNET to an isolated .NET 11 SDK dotnet executable.");
        var info = new ProcessStartInfo(sdk, "--version") { UseShellExecute = false, RedirectStandardOutput = true };
        using Process process = Process.Start(info) ?? throw new AutomationException("Cannot query the ReadyToRun SDK.");
        string version = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0 || !version.StartsWith("11.", StringComparison.Ordinal))
            throw new AutomationException($"Experimental iOS ReadyToRun requires .NET 11; selected SDK reports '{version}'.");
        return sdk;
    }

    internal static void Publish(string input, string output)
    {
        string sdk = ValidateSdk();
        var properties = new XElement("PropertyGroup",
            new XElement("TargetFramework", "net11.0"),
            new XElement("OutputType", "Exe"),
            new XElement("UseAppHost", "false"),
            new XElement("UseMonoRuntime", "false"),
            new XElement("SelfContained", "true"),
            new XElement("PublishTrimmed", "false"),
            new XElement("PublishReadyToRun", "true"),
            new XElement("PublishReadyToRunComposite", "true"),
            new XElement("PublishReadyToRunContainerFormat", "macho"),
            new XElement("AppleMinOSVersion", "15.0"),
            new XElement("RuntimeIdentifier", "ios-arm64"),
            new XElement("EnableDefaultCompileItems", "false"));
        var items = new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", "R2REntry.cs")));
        foreach (string file in Directory.GetFiles(input, "*.dll"))
        {
            // The input is a framework-dependent target publish, never the Editor output.
            string name = System.Reflection.AssemblyName.GetAssemblyName(file).Name!;
            items.Add(new XElement("Reference", new XAttribute("Include", name),
                new XElement("HintPath", file), new XElement("Private", "true")));
        }
        foreach (string file in Directory.GetFiles(input, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (ext is ".dll" or ".pdb" || file.EndsWith(".deps.json") || file.EndsWith(".runtimeconfig.json")) continue;
            items.Add(new XElement("Content", new XAttribute("Include", file),
                new XElement("Link", Path.GetRelativePath(input, file)), new XElement("CopyToPublishDirectory", "PreserveNewest")));
        }
        // Keep the aggregate out of the input directory so future content scans cannot ingest its sources.
        string projectDir = input + "-Aggregate";
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "R2REntry.cs"), "internal static class R2REntry { public static void Main() {} }");
        string project = Path.Combine(projectDir, "UnrealSharpR2R.csproj");
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties, items)).Save(project);
        Run(sdk, projectDir, "publish", project, "-c", "Release", "-o", output);
        if (!File.Exists(Path.Combine(output, "UnrealSharpR2R.r2r.dylib")) || !File.Exists(Path.Combine(output, "libcoreclr.dylib")))
            throw new AutomationException("ReadyToRun publication did not produce the iOS composite and CoreCLR runtime.");

        string[] libraries = Directory.GetFiles(output, "*.dylib");
        foreach (string library in libraries)
        {
            string name = Path.GetFileNameWithoutExtension(library);
            if (name == "UnrealSharpR2R.r2r") name = "UnrealSharpR2R";
            string framework = Path.Combine(output, "Frameworks", name + ".framework");
            Directory.CreateDirectory(framework);
            string binary = Path.Combine(framework, name);
            File.Move(library, binary, overwrite: true);
            // Xcode's CodeSignOnCopy preserves an existing signing identifier.
            // Runtime-pack dylibs carry their original library identifier, which
            // iOS rejects for our framework bundle. Sign the new bundle in Xcode.
            Run("/usr/bin/codesign", output, "--remove-signature", binary);
            var installNameArguments = new List<string> { "install_name_tool", "-id", "@rpath/" + name + ".framework/" + name };
            foreach (string dependency in libraries)
            {
                string dependencyName = Path.GetFileNameWithoutExtension(dependency);
                if (dependencyName == "UnrealSharpR2R.r2r") dependencyName = "UnrealSharpR2R";
                installNameArguments.AddRange(new[] { "-change", "@rpath/" + Path.GetFileName(dependency),
                    "@rpath/" + dependencyName + ".framework/" + dependencyName });
            }
            installNameArguments.Add(binary);
            Run("/usr/bin/xcrun", output, installNameArguments.ToArray());
            var plist = new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
                new XElement("key", "CFBundleIdentifier"), new XElement("string", "com.unrealsharp.r2r." + name.Replace('.', '-')),
                new XElement("key", "CFBundleExecutable"), new XElement("string", name),
                new XElement("key", "CFBundlePackageType"), new XElement("string", "FMWK"),
                new XElement("key", "CFBundleVersion"), new XElement("string", "1.0"),
                new XElement("key", "CFBundleShortVersionString"), new XElement("string", "1.0"),
                new XElement("key", "MinimumOSVersion"), new XElement("string", "15.0")));
            new XDocument(plist).Save(Path.Combine(framework, "Info.plist"));
        }
        foreach (string archive in Directory.GetFiles(output, "*.a")) File.Delete(archive);
        File.WriteAllText(Path.Combine(output, "UnrealSharp.ReadyToRun"), "ios-arm64/coreclr/net11.0");
    }

    private static void Run(string executable, string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false };
        if (Path.GetFileName(executable) == "dotnet")
        {
            // UAT runs with UE's bundled SDK. Its MSBuild overrides must not
            // redirect this explicitly selected publisher back to that SDK.
            foreach (string variable in new[] { "MSBUILD_EXE_PATH", "MSBuildSDKsPath", "MSBuildExtensionsPath",
                "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR", "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER" })
                info.Environment.Remove(variable);
            info.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(executable);
            info.Environment["DOTNET_ROOT_ARM64"] = Path.GetDirectoryName(executable);
            info.Environment["DOTNET_HOST_PATH"] = executable;
            info.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        }
        info.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        info.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info) ?? throw new AutomationException("Cannot start " + executable);
        process.WaitForExit();
        if (process.ExitCode != 0) throw new AutomationException($"{executable} failed with exit code {process.ExitCode}.");
    }
}
