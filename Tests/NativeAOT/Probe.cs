using System.Runtime.CompilerServices;
using UnrealSharp.Attributes;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine.Core.Modules;

namespace NativeAotProbe;

[UModule]
public class FNativeAotProbe : IModuleInterface
{
    public void StartupModule() => Console.WriteLine("NATIVEAOT_MODULE START dynamic=" + RuntimeFeature.IsDynamicCodeSupported);
    public void ShutdownModule() => Console.WriteLine("NATIVEAOT_MODULE STOP");
}

[UClass]
public partial class UNativeAotProbe : UObject
{
    [UProperty(PropertyFlags.EditAnywhere | PropertyFlags.BlueprintReadWrite)]
    public partial int StoredValue { get; set; }

    public UNativeAotProbe() { StoredValue = 11; }

    [UFunction(FunctionFlags.BlueprintCallable)]
    public int Add(int lhs, int rhs) => lhs + rhs;

    [UFunction(FunctionFlags.BlueprintCallable)]
    public int NativePropertyRoundtrip(int value) { StoredValue = value; return StoredValue; }

    [UFunction(FunctionFlags.BlueprintCallable)]
    public void Adjust(ref int value, out int doubled) { value += 3; doubled = value * 2; }

    [UFunction(FunctionFlags.BlueprintCallable)]
    public string Echo(string text) => text;

    [UFunction(FunctionFlags.BlueprintCallable)]
    public string RuntimeMode() => RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT";

    [UFunction(FunctionFlags.BlueprintCallable)]
    public int HotReloadValue() => 101;
}
