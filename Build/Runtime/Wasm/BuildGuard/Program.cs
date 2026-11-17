using System;
using System.Collections.Generic;
using System.IO;
using UnrealSharp.Wasm;

try
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    if (args.Length % 2 != 0) throw new InvalidDataException("Expected explicit guard option/value pairs.");
    for (int index = 0; index < args.Length; index += 2)
        if (!options.TryAdd(args[index], args[index + 1])) throw new InvalidDataException("Duplicate guard option.");
    string Read(string key) => options.TryGetValue(key, out string? value) ? value : throw new InvalidDataException("Missing guard option: " + key);
    if (options.Count != 7) throw new InvalidDataException("Unexpected guard options.");
    string platform = Read("--platform");
    if (platform != "Wasm") return 0;
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The static CoreCLR prebuild guard currently supports Windows hosts.");
    var profile = new WasmBuildProfile(WasmData.Full(Read("--project")), WasmData.Full(Read("--plugin")), WasmData.Full(Read("--engine")),
        Read("--target"), Read("--type"), platform, Read("--configuration"), "wasm64", true, true);
    bool codegenOnly = WindowsUbtInvocation.Validate(profile);
    WasmBuildInput input = WasmBuildInput.Load(profile.InputFile, profile);
    input.RuntimeProduct.RequireUeExceptionAbi();
    using IDisposable lease = input.AcquireBuildLease();
    if (codegenOnly)
    {
        input.RequireUnchanged();
        Console.WriteLine("UnrealSharp Wasm guard: actual standalone UBT -SkipBuild; matching runtime product verified; application preparation follows UHT.");
        return 0;
    }
    WasmPreparedApp ready = WasmPreparedApp.Load(input);
    ready.RequireUnchanged();
    Console.WriteLine("UnrealSharp Wasm guard: complete prepared app verified " + ready.ReadyKey);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("UnrealSharp Wasm prebuild rejected: " + error.Message);
    return 1;
}
