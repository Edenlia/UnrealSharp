using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using UnrealSharp.Wasm;

internal static class WindowsUbtInvocation
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string command, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);

    internal static string[] Split(string command)
    {
        IntPtr memory = CommandLineToArgvW(command, out int count);
        if (memory == IntPtr.Zero) throw new InvalidDataException("Cannot parse Windows process arguments.");
        try { return Enumerable.Range(0, count).Select(index => Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, index * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(memory); }
    }

    internal static bool Validate(WasmBuildProfile profile)
    {
        string workingDirectory = VerifiedWorkingDirectory(profile);
        // Query the real active ancestry rather than accepting a caller/env flag.
        string ubtDll = Path.Combine(profile.EngineDirectory, "Binaries", "DotNET", "UnrealBuildTool", "UnrealBuildTool.dll");
        string ubtExe = Path.ChangeExtension(ubtDll, ".exe");
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(ubtDll));
        string command = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false); $expected=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "')); $r=@(); $n=" + Environment.ProcessId + "; $s=@{}; for($i=0;$i -lt 24;$i++){if($s.ContainsKey($n)){throw 'Process ancestry cycle'};$s[$n]=$true;$p=Get-CimInstance Win32_Process -Filter ('ProcessId='+$n);if(!$p){break};$r+=@{Pid=[int]$p.ProcessId;Parent=[int]$p.ParentProcessId;Created=$p.CreationDate.ToUniversalTime().Ticks;Image=$p.ExecutablePath;Command=$p.CommandLine};if($p.ParentProcessId -eq 0 -or ($p.Name -eq 'UnrealBuildTool.exe' -and $p.ExecutablePath -eq [IO.Path]::ChangeExtension($expected,'.exe')) -or ($p.Name -eq 'dotnet.exe' -and $p.CommandLine -and $p.CommandLine.IndexOf([IO.Path]::GetFileName($expected),[StringComparison]::OrdinalIgnoreCase) -ge 0)){break};$n=[int]$p.ParentProcessId};ConvertTo-Json -InputObject $r -Compress";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) start.ArgumentList.Add(argument);
        using Process child = Process.Start(start) ?? throw new InvalidDataException("Cannot inspect UBT process ancestry.");
        string output = child.StandardOutput.ReadToEnd(), error = child.StandardError.ReadToEnd();
        child.WaitForExit(); if (child.ExitCode != 0) throw new InvalidDataException("Cannot verify UBT ancestry: " + error);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement[] processes = json.RootElement.EnumerateArray().ToArray();
        WasmData.Require(processes.Length > 1 && processes[0].GetProperty("Pid").GetInt32() == Environment.ProcessId, "Invalid current process identity.");
        for (int index = 0; index + 1 < processes.Length; index++)
            WasmData.Require(processes[index].GetProperty("Parent").GetInt32() == processes[index + 1].GetProperty("Pid").GetInt32() &&
                processes[index + 1].GetProperty("Created").GetInt64() <= processes[index].GetProperty("Created").GetInt64(), "Process ancestry was reused or changed.");
        foreach (JsonElement process in processes.Skip(1))
        {
            string image = process.GetProperty("Image").GetString() ?? "";
            string text = process.GetProperty("Command").GetString() ?? "";
            if (image.Length == 0 || text.Length == 0) continue;
            string[] arguments = Split(text);
            bool standalone = WasmData.Paths.Equals(image, ubtExe);
            if (!standalone && !Path.GetFileName(image).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) continue;
            int entry = standalone ? 0 : FindDotnetEntry(arguments, ubtDll, workingDirectory);
            if (!standalone && entry < 0) continue;
            using Process actual = Process.GetProcessById(process.GetProperty("Pid").GetInt32());
            // CIM_DATETIME preserves microseconds; Process.StartTime has 100ns precision.
            WasmData.Require(actual.StartTime.ToUniversalTime().Ticks / 10 == process.GetProperty("Created").GetInt64() / 10 &&
                WasmData.Paths.Equals(actual.MainModule!.FileName, image) && File.Exists(ubtDll), "UBT process identity changed.");
            return CheckArguments(arguments.Skip(entry + 1).ToArray(), profile);
        }
        throw new InvalidDataException("A real matching-engine UBT ancestor is required; direct guard invocation cannot bypass app readiness.");
    }

    private static string VerifiedWorkingDirectory(WasmBuildProfile profile)
    {
        // UBT sets Engine/Source before executing modes; custom build steps and
        // this plugin's bootstrap inherit it without changing directory.
        string current = Path.GetFullPath(Environment.CurrentDirectory);
        WasmData.Require(WasmData.Paths.Equals(current, Path.Combine(profile.EngineDirectory, "Source")), "The prebuild guard must inherit the configured engine's Source working directory.");
        return current;
    }

    private static int FindDotnetEntry(string[] arguments, string ubtDll, string workingDirectory)
    {
        int entry = arguments.Length > 1 && arguments[1] == "exec" ? 2 : 1;
        return arguments.Length > entry && WasmData.Paths.Equals(Path.GetFullPath(arguments[entry], workingDirectory), ubtDll) ? entry : -1;
    }

    internal static bool CheckArguments(string[] arguments, WasmBuildProfile profile)
    {
        string workingDirectory = VerifiedWorkingDirectory(profile);
        WasmData.Require(!arguments.Any(value => value.StartsWith("-TargetList", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("-Mode=", StringComparison.OrdinalIgnoreCase) && !value.Equals("-Mode=Build", StringComparison.OrdinalIgnoreCase)), "Unsupported UBT invocation mode/target list.");
        string[] nested = arguments.Where(value => value.StartsWith("-Target=", StringComparison.OrdinalIgnoreCase)).ToArray();
        string[][] targets = nested.Length == 0 ? [arguments] : nested.Select(value => Split("target " + value[8..]).Skip(1).ToArray()).ToArray();
        bool Matches(string[] target)
        {
            int count = 0;
            for (int index = 0; index + 2 < target.Length; index++)
                if (target[index] == profile.TargetName && target[index + 1] == profile.Platform && target[index + 2] == profile.Configuration) count++;
            return count == 1;
        }
        string[][] matching = targets.Where(Matches).ToArray();
        WasmData.Require(matching.Length == 1, "UBT does not contain exactly one matching native Game target.");
        string[] selected = matching[0];
        string[] effective = selected.Concat(nested.Length == 0 ? [] : arguments.Where(value => !value.StartsWith("-Target=", StringComparison.OrdinalIgnoreCase))).ToArray();
        string[] projects = effective.Where(value => value.StartsWith("-Project=", StringComparison.OrdinalIgnoreCase) ||
            !value.StartsWith("-", StringComparison.Ordinal) && Path.GetExtension(value).Equals(".uproject", StringComparison.OrdinalIgnoreCase))
            .Select(value => value.StartsWith("-Project=", StringComparison.OrdinalIgnoreCase) ? value[9..] : value).ToArray();
        WasmData.Require(projects.Length == 1 && WasmData.Paths.Equals(Path.GetFullPath(projects[0], workingDirectory), profile.ProjectFile), "UBT project differs from the frozen Wasm request.");
        string[] arches = effective.Where(value => value.StartsWith("-architecture=", StringComparison.OrdinalIgnoreCase)).Select(value => value[14..]).ToArray();
        WasmData.Require(arches.Length <= 1 && arches.All(value => value.Equals("wasm64", StringComparison.OrdinalIgnoreCase)) &&
            !effective.Any(value => value.Equals("-NoWasmLowerMemory64", StringComparison.OrdinalIgnoreCase) || value.StartsWith("-Wasm64=", StringComparison.OrdinalIgnoreCase)), "UBT architecture/ABI differs from the frozen Wasm profile.");
        return targets.Length == 1 && arguments.Any(value => value.Equals("-SkipBuild", StringComparison.OrdinalIgnoreCase));
    }
}
