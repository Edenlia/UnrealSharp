using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnrealSharp.Wasm;

internal static class MacUbtInvocation
{
    private const string LibProc = "/usr/lib/libproc.dylib";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private const int ProcPidTBsdInfo = 3, ProcPidPathMaxSize = 4096;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    // Darwin's public proc_bsdinfo ABI (sys/proc_info.h); unrelated fields are omitted.
    [StructLayout(LayoutKind.Explicit, Size = 136)]
    private struct BsdInfo
    {
        [FieldOffset(12)] internal uint Pid;
        [FieldOffset(16)] internal uint Parent;
        [FieldOffset(120)] internal ulong StartSeconds;
        [FieldOffset(128)] internal ulong StartMicroseconds;
    }

    [DllImport(LibProc, SetLastError = true)]
    private static extern int proc_pidinfo(int pid, int flavor, ulong arg, out BsdInfo buffer, int bufferSize);
    [DllImport(LibProc, SetLastError = true)]
    private static extern int proc_pidpath(int pid, [Out] byte[] buffer, uint bufferSize);
    [DllImport(LibSystem, SetLastError = true)]
    private static extern int sysctl(int[] name, uint nameLength, [Out] byte[] oldValue, ref nuint oldLength, IntPtr newValue, nuint newLength);

    private sealed record Snapshot(int Pid, int Parent, ulong StartSeconds, ulong StartMicroseconds, string Image, string[] Arguments);

    internal static bool Validate(WasmBuildProfile profile)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Darwin UBT ancestry inspection requires macOS.");
        string workingDirectory = UbtInvocation.VerifiedWorkingDirectory(profile);
        string ubtDll = Path.Combine(profile.EngineDirectory, "Binaries", "DotNET", "UnrealBuildTool", "UnrealBuildTool.dll");
        string ubtExecutable = Path.Combine(Path.GetDirectoryName(ubtDll)!, "UnrealBuildTool");
        var processes = new List<Snapshot>();
        var visited = new HashSet<int>();
        int pid = Environment.ProcessId;
        for (int depth = 0; depth < 24 && pid > 0; depth++)
        {
            WasmData.Require(visited.Add(pid), "Process ancestry cycle.");
            Snapshot process = ReadSnapshot(pid);
            processes.Add(process);
            if (processes.Count > 1)
            {
                Snapshot descendant = processes[^2];
                WasmData.Require(descendant.Parent == process.Pid && StartsNoLater(process, descendant), "Process ancestry was reused or changed.");
                bool standalone = WasmData.Paths.Equals(process.Image, ubtExecutable);
                bool dotnet = Path.GetFileName(process.Image).Equals("dotnet", StringComparison.Ordinal);
                int entry = standalone ? 0 : dotnet ? UbtInvocation.FindDotnetEntry(process.Arguments, ubtDll, workingDirectory) : -1;
                if (entry >= 0)
                {
                    // KERN_PROCARGS2 pads before argv[0]; an empty argv[0] is not a
                    // supported .NET/UBT launch. Bind the parsed first argument to
                    // the kernel image before interpreting subsequent DLL/options.
                    WasmData.Require(process.Arguments.Length > 0 &&
                        Path.GetFileName(process.Arguments[0]).Equals(Path.GetFileName(process.Image), StringComparison.Ordinal), "UBT process argument zero does not identify its kernel executable.");
                    WasmData.Require(File.Exists(ubtDll), "The matching-engine UBT entry does not exist.");
                    // Re-read the complete live chain, not just the matched PID: a parent can exit,
                    // be reparented or exec between the independent kernel queries above.
                    foreach (Snapshot original in processes)
                    {
                        Snapshot actual = ReadSnapshot(original.Pid);
                        WasmData.Require(actual.Parent == original.Parent && actual.StartSeconds == original.StartSeconds &&
                            actual.StartMicroseconds == original.StartMicroseconds && WasmData.Paths.Equals(actual.Image, original.Image) &&
                            actual.Arguments.SequenceEqual(original.Arguments, StringComparer.Ordinal), "UBT process ancestry identity changed.");
                    }
                    return UbtInvocation.CheckArguments(process.Arguments.Skip(entry + 1).ToArray(), profile);
                }
            }
            pid = process.Parent;
        }
        throw new InvalidDataException("A real matching-engine UBT ancestor is required; direct guard invocation cannot bypass app readiness.");
    }

    private static bool StartsNoLater(Snapshot ancestor, Snapshot descendant) => ancestor.StartSeconds < descendant.StartSeconds ||
        ancestor.StartSeconds == descendant.StartSeconds && ancestor.StartMicroseconds <= descendant.StartMicroseconds;

    private static Snapshot ReadSnapshot(int pid)
    {
        int size = Marshal.SizeOf<BsdInfo>();
        WasmData.Require(proc_pidinfo(pid, ProcPidTBsdInfo, 0, out BsdInfo info, size) == size && info.Pid == (uint)pid && info.StartMicroseconds < 1000000,
            "Cannot verify Darwin process identity for PID " + pid + ".");
        byte[] path = new byte[ProcPidPathMaxSize];
        int pathLength = proc_pidpath(pid, path, (uint)path.Length);
        WasmData.Require(pathLength > 0 && pathLength < path.Length, "Cannot verify Darwin process executable for PID " + pid + ".");
        int pathEnd = Array.IndexOf(path, (byte)0, 0, pathLength);
        string image = Utf8.GetString(path, 0, pathEnd < 0 ? pathLength : pathEnd);
        WasmData.Require(Path.IsPathFullyQualified(image), "Darwin process executable is not absolute.");
        string[] arguments = ReadArguments(pid);
        return new Snapshot(pid, checked((int)info.Parent), info.StartSeconds, info.StartMicroseconds, image, arguments);
    }

    private static string[] ReadArguments(int pid)
    {
        // KERN_PROCARGS2 returns argc, the executable path, NUL padding, argv, then
        // the environment. Read exactly argc strings; never use or expose the environment.
        byte[] argMaxBytes = new byte[sizeof(int)];
        nuint argMaxLength = (nuint)argMaxBytes.Length;
        WasmData.Require(sysctl([1, 8], 2, argMaxBytes, ref argMaxLength, IntPtr.Zero, 0) == 0 && argMaxLength == sizeof(int), "Cannot read Darwin argument size limit.");
        int argMax = BitConverter.ToInt32(argMaxBytes);
        WasmData.Require(argMax >= sizeof(int) && argMax <= 16 * 1024 * 1024, "Invalid Darwin argument size limit.");
        byte[] bytes = new byte[argMax];
        nuint length = (nuint)bytes.Length;
        WasmData.Require(sysctl([1, 49, pid], 3, bytes, ref length, IntPtr.Zero, 0) == 0 && length >= sizeof(int) && length <= (nuint)bytes.Length,
            "Cannot read Darwin process arguments for PID " + pid + ".");
        int limit = checked((int)length), count = BitConverter.ToInt32(bytes);
        WasmData.Require(count > 0 && count <= limit - sizeof(int), "Invalid Darwin process argument count.");
        int cursor = sizeof(int);
        ReadString(); // Kernel executable path precedes argv.
        while (cursor < limit && bytes[cursor] == 0) cursor++;
        var arguments = new string[count];
        for (int index = 0; index < count; index++) arguments[index] = ReadString();
        return arguments;

        string ReadString()
        {
            WasmData.Require(cursor < limit, "Truncated Darwin process arguments.");
            int end = Array.IndexOf(bytes, (byte)0, cursor, limit - cursor);
            WasmData.Require(end >= cursor, "Unterminated Darwin process argument.");
            string value = Utf8.GetString(bytes, cursor, end - cursor);
            cursor = end + 1;
            return value;
        }
    }
}
