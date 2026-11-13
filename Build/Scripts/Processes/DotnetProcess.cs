using System.IO;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharp.Automation.Processes;

public class DotnetProcess : BuildToolProcess
{
    public DotnetProcess() : this(DotNetUtilities.DotNetExecutable, DotNetUtilities.LatestDotNetSdkPath)
    {
    }

    /// <summary>Runs a specific SDK through a specific muxer, independent of global.json and the default SDK.</summary>
    public DotnetProcess(string dotnetExecutable, string sdkPath) : base(dotnetExecutable)
    {
        StartInfo.ArgumentList.Add(Path.Combine(sdkPath, "dotnet.dll"));
        StartInfo.Environment["MSBuildExtensionsPath"] = sdkPath;
        StartInfo.Environment["MSBUILD_EXE_PATH"] = Path.Combine(sdkPath, "MSBuild.dll");
        StartInfo.Environment["MSBuildSDKsPath"] = Path.Combine(sdkPath, "Sdks");
        StartInfo.Environment["DOTNET_ROLL_FORWARD"] = "LatestMinor";
    }
}
