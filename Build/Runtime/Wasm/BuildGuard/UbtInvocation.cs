using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnrealSharp.Wasm;

internal static class UbtInvocation
{
    internal static string VerifiedWorkingDirectory(WasmBuildProfile profile)
    {
        // UBT sets Engine/Source before executing modes; custom build steps and
        // this plugin's bootstrap inherit it without changing directory.
        string current = Path.GetFullPath(Environment.CurrentDirectory);
        WasmData.Require(WasmData.Paths.Equals(current, Path.Combine(profile.EngineDirectory, "Source")), "The prebuild guard must inherit the configured engine's Source working directory.");
        return current;
    }

    internal static int FindDotnetEntry(string[] arguments, string ubtDll, string workingDirectory)
    {
        int entry = arguments.Length > 1 && arguments[1] == "exec" ? 2 : 1;
        return arguments.Length > entry && WasmData.Paths.Equals(Path.GetFullPath(arguments[entry], workingDirectory), ubtDll) ? entry : -1;
    }

    internal static string[] SplitTarget(string commandLine)
    {
        // TargetDescriptor.ParseCommandLine uses EpicGames.Core.CommandLineArguments.Split,
        // including on POSIX hosts. This deliberately is not shell or CommandLineToArgvW parsing.
        // Keep the same two passes and -Define: quote exception without loading Engine assemblies.
        var arguments = new List<string>();
        var argument = new StringBuilder();
        for (int index = 0; index < commandLine.Length; index++)
        {
            if (char.IsWhiteSpace(commandLine[index])) continue;
            argument.Clear();
            for (bool inQuotes = false; index < commandLine.Length; index++)
            {
                if (commandLine[index] == '"') inQuotes = !inQuotes;
                else if (!inQuotes && char.IsWhiteSpace(commandLine[index])) break;
                argument.Append(commandLine[index]);
            }
            arguments.Add(argument.ToString());
        }
        for (int index = 0; index < arguments.Count; index++)
        {
            string value = arguments[index];
            if (value.StartsWith("-Define:", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("-Define:\"", StringComparison.OrdinalIgnoreCase)) continue;
            arguments[index] = value.Replace("\"", "", StringComparison.OrdinalIgnoreCase);
        }
        return arguments.ToArray();
    }

    internal static bool CheckArguments(string[] arguments, WasmBuildProfile profile)
    {
        string workingDirectory = VerifiedWorkingDirectory(profile);
        static void RequireSupported(string[] values) => WasmData.Require(!values.Any(value => value.StartsWith("-TargetList", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("-Mode=", StringComparison.OrdinalIgnoreCase) && !value.Equals("-Mode=Build", StringComparison.OrdinalIgnoreCase)), "Unsupported UBT invocation mode/target list.");
        RequireSupported(arguments);
        string[] nested = arguments.Where(value => value.StartsWith("-Target=", StringComparison.OrdinalIgnoreCase)).ToArray();
        string[][] targets = nested.Length == 0 ? [arguments] : nested.Select(value => SplitTarget(value[8..])).ToArray();
        foreach (string[] target in targets)
        {
            RequireSupported(target);
            WasmData.Require(nested.Length == 0 || !target.Any(value => value.StartsWith("-Target=", StringComparison.OrdinalIgnoreCase)), "Recursively nested UBT targets are unsupported by the prebuild guard.");
        }
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
