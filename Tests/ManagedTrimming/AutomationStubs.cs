using System.Diagnostics;

namespace UnrealSharp.Automation.Utilities
{
    internal static class DotNetUtilities
    {
        internal const string Version = "net10.0";
    }

    internal static class LoggerUtilities
    {
        internal static void LogUnrealSharpInfo(string message) => Console.WriteLine(message);
    }
}

namespace UnrealSharp.Automation.Processes
{
    internal sealed class DotnetProcess : Process
    {
        internal DotnetProcess() => StartInfo.FileName = "dotnet";

        internal void StartProcess()
        {
            Start();
            WaitForExit();
            if (ExitCode != 0)
            {
                throw new InvalidOperationException($"dotnet exited with code {ExitCode}.");
            }
        }
    }
}
