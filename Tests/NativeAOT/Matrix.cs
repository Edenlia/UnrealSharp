using UnrealSharp;
using UnrealSharp.Attributes;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using System.Runtime.InteropServices;

namespace NativeAotProbe;

[UEnum]
public enum EProbeMode : byte { First, Second }

[UStruct]
public partial struct FProbeData
{
    [UProperty] public int Number { get; set; }
    [UProperty] public string Label { get; set; }
    [UProperty] public FVector Position { get; set; }
}

[USingleDelegate]
public delegate void FProbeSignal(int value);

[UMultiDelegate]
public delegate void FProbeBroadcast(int value);

[UInterface]
public partial interface IProbeIdentity
{
    [UFunction(FunctionFlags.BlueprintCallable)]
    public int Identify();
    [UFunction(FunctionFlags.BlueprintCallable)]
    public int AddToIdentity(int value);
    [UFunction(FunctionFlags.BlueprintCallable)]
    public void SetSignal();
}

[UClass]
public partial class UNativeAotDerived : UNativeAotProbe
{
    [UFunction] public int ReadInherited() => StoredValue;
}

[UClass]
public partial class UNativeAotTickProbe : UActorComponent
{
    [UProperty(PropertyFlags.EditAnywhere | PropertyFlags.BlueprintReadWrite)] public partial int TickValue { get; set; }
    public override void Tick(float deltaSeconds) => TickValue = (int)(deltaSeconds * 100);
}

[UClass]
public partial class UNativeAotMatrix : UObject, IProbeIdentity
{
    public static int DisposedCount;
    public override void Dispose() { ++DisposedCount; base.Dispose(); }

    [UProperty] public partial bool BoolValue { get; set; }
    [UProperty] public partial byte ByteValue { get; set; }
    [UProperty] public partial long LongValue { get; set; }
    [UProperty] public partial float FloatValue { get; set; }
    [UProperty] public partial double DoubleValue { get; set; }
    [UProperty] public partial EProbeMode EnumValue { get; set; }
    [UProperty] public partial string StringValue { get; set; }
    [UProperty] public partial FName NameValue { get; set; }
    [UProperty] public partial FText TextValue { get; set; }
    [UProperty] public partial FProbeData StructValue { get; set; }
    [UProperty] public partial IList<int> Ints { get; set; }
    [UProperty] public partial IList<FProbeData> Structs { get; set; }
    [UProperty] public partial IDictionary<FName, string> Map { get; set; }
    [UProperty] public partial ISet<int> Set { get; set; }
    [UProperty] public partial UObject? ObjectValue { get; set; }
    [UProperty] public partial TWeakObjectPtr<UObject> WeakValue { get; set; }
    [UProperty] public partial TSoftObjectPtr<UObject> SoftValue { get; set; }
    [UProperty] public partial IProbeIdentity? InterfaceValue { get; set; }
    [UProperty] public partial TMulticastDelegate<FProbeBroadcast> Broadcast { get; set; }
    private int _signalValue;

    [UFunction] public void OnSignal(int value) => _signalValue = value;
    public int Identify() => 73;
    public int AddToIdentity(int value) => 73 + value;
    public void SetSignal() => _signalValue = 81;
    [UFunction] public static int StaticAdd(int lhs, int rhs) => lhs + rhs;
    [UFunction] public static int GetDisposedCount() => DisposedCount;
    [UFunction] public void ThrowExpected() => throw new InvalidOperationException("NATIVEAOT_EXPECTED_EXCEPTION");
    [DllImport("NativeAotDependency", EntryPoint = "NativeAotDependencyAnswer")]
    private static extern int NativeDependencyAnswer();

    [UFunction]
    public int CheckPublishedContent()
    {
        string content = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "nativeaot-probe.txt")).Trim();
        return content == "NativeAOT published content: 中文" ? NativeDependencyAnswer() : -1;
    }

    [UFunction]
    public int RunMatrix()
    {
        int mask = 0;
        void Check(int bit, string name, Func<bool> action)
        {
            try
            {
                bool passed = action();
                if (passed) mask |= 1 << bit;
                Console.WriteLine($"NATIVEAOT_CASE {name}={(passed ? "PASS" : "FAIL")}");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"NATIVEAOT_CASE {name}=FAIL {exception}");
            }
        }

        Check(0, "primitives", () =>
        {
            BoolValue = true; ByteValue = 231; LongValue = 0x123456789L;
            FloatValue = 1.25f; DoubleValue = -2.5; EnumValue = EProbeMode.Second;
            return BoolValue && ByteValue == 231 && LongValue == 0x123456789L &&
                FloatValue == 1.25f && DoubleValue == -2.5 && EnumValue == EProbeMode.Second;
        });
        Check(1, "string_name_text", () =>
        {
            StringValue = "AOT 中文 café"; NameValue = new FName("AotProbe_Name");
            using FText text = new("本地化 text");
            TextValue = text;
            using FText read = TextValue;
            return StringValue == "AOT 中文 café" && NameValue.ToString() == "AotProbe_Name" && read.ToString() == "本地化 text";
        });
        Check(2, "structs", () =>
        {
            StructValue = new FProbeData { Number = 42, Label = "结构体", Position = new FVector(1, 2, 3) };
            FProbeData read = StructValue;
            return read.Number == 42 && read.Label == "结构体" && read.Position == new FVector(1, 2, 3);
        });
        Check(3, "containers", () =>
        {
            Ints.Clear(); Ints.Add(3); Ints.Add(7); Ints[0] = 5;
            Structs.Clear(); Structs.Add(new FProbeData { Number = 17, Label = "array", Position = new FVector(4, 5, 6) });
            Map.Clear(); Map.Add(new FName("first"), "映射");
            Set.Clear(); Set.Add(11); Set.Add(11); Set.Add(19); Set.Remove(19);
            return Ints.Count == 2 && Ints[0] + Ints[1] == 12 && Structs[0].Number == 17 &&
                Structs[0].Label == "array" && Map[new FName("first")] == "映射" && Set.Count == 1 && Set.Contains(11);
        });
        Check(4, "object_references", () =>
        {
            UObject child = NewObject<UNativeAotProbe>(this);
            ObjectValue = child; WeakValue = new TWeakObjectPtr<UObject>(child); SoftValue = new TSoftObjectPtr<UObject>(child);
            return ReferenceEquals(ObjectValue, child) && WeakValue.IsValid && ReferenceEquals(WeakValue.Object, child) &&
                ReferenceEquals(SoftValue.Object, child);
        });
        Check(5, "single_delegate", () =>
        {
            TDelegate<FProbeSignal> signal = new(); signal.Add(OnSignal);
            signal.InnerDelegate.Invoke(27);
            bool passed = signal.IsBound && signal.Contains(OnSignal) && _signalValue == 27;
            signal.Remove(OnSignal);
            return passed && !signal.IsBound;
        });
        Check(6, "multicast_delegate", () =>
        {
            Broadcast.Clear(); Broadcast.Add(OnSignal); Broadcast.InnerDelegate.Invoke(39);
            bool passed = Broadcast.Contains(OnSignal) && _signalValue == 39;
            Broadcast.Remove(OnSignal);
            return passed && !Broadcast.Contains(OnSignal);
        });
        Check(7, "interface", () =>
        {
            InterfaceValue = this;
            InterfaceValue!.SetSignal();
            return InterfaceValue.Identify() == 73 && InterfaceValue.AddToIdentity(9) == 82 && _signalValue == 81;
        });
        Check(8, "inheritance", () =>
        {
            UNativeAotDerived derived = NewObject<UNativeAotDerived>(this);
            return derived.ReadInherited() == 11 && derived.Add(13, 29) == 42;
        });
        Check(9, "managed_gc", () =>
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            return NativeObject != IntPtr.Zero && ObjectValue != null && WeakValue.IsValid &&
                StructValue.Number == 42 && Ints[1] == 7 && InterfaceValue!.Identify() == 73;
        });
        return mask;
    }
}
