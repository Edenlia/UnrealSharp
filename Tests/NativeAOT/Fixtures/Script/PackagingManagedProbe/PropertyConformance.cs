using UnrealSharp.Attributes;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;

namespace PackagingManagedProbe;

[UStruct]
public partial struct FPropertyProbeData
{
    [UProperty] public int Count { get; set; }
    [UProperty] public string Label { get; set; }
    [UProperty] public FVector Position { get; set; }

    public FPropertyProbeData()
    {
        Count = 11;
        Label = "struct default: 中文";
        Position = new FVector(1, 2, 3);
    }
}

[UClass]
public partial class UPropertyProbe : UObject
{
    [UProperty] public partial bool Enabled { get; set; }
    [UProperty] public partial int Number { get; set; }
    [UProperty] public partial string Text { get; set; }
    [UProperty] public partial FName NameValue { get; set; }
    [UProperty] public partial UObject? Reference { get; set; }
    [UProperty] public partial FPropertyProbeData Data { get; set; }

    public UPropertyProbe()
    {
        Number = 73;
        Text = "constructor";
    }

    [UFunction(FunctionFlags.BlueprintEvent)]
    public partial FPropertyProbeData ExchangeData(FPropertyProbeData input, ref FPropertyProbeData changed, out FPropertyProbeData output);

    public partial FPropertyProbeData ExchangeData_Implementation(FPropertyProbeData input, ref FPropertyProbeData changed, out FPropertyProbeData output)
    {
        changed.Count += input.Count;
        changed.Label += ":ref";
        output = input;
        output.Label += ":out";
        input.Count *= 2;
        return input;
    }
}

public static class PropertyConformance
{
    public static void Run(Action<bool, string> check)
    {
        UPropertyProbe obj = UObject.NewObject<UPropertyProbe>();
        check(obj.NativeObject != IntPtr.Zero && obj.Number == 73 && obj.Text == "constructor",
            "managed constructor writes native-backed properties");
        obj.Enabled = true;
        obj.Number = -29;
        obj.Text = "property: 中文 🚀";
        obj.NameValue = new FName("ProbePropertyName");
        check(obj.Enabled && obj.Number == -29 && obj.Text == "property: 中文 🚀" && obj.NameValue == new FName("ProbePropertyName"),
            "generated bool/int/string/name properties");
        obj.Enabled = false;
        obj.Text = string.Empty;
        check(!obj.Enabled && obj.Text.Length == 0, "generated properties clear bool and retained FString");
        obj.Reference = UObject.NewObject<UFunctionBaseProbe>();
        UObject referenced = obj.Reference;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        check(obj.Reference == referenced, "generated UObject property preserves identity after managed GC");
        obj.Reference = null;
        check(obj.Reference == null, "generated UObject property clears to null");

        FPropertyProbeData data = obj.Data;
        check(data.Count == 11 && data.Label == "struct default: 中文" && data.Position.Z == 3,
            "native UStruct initialization invokes managed default constructor");
        data.Count = 7;
        data.Label = "payload: 中文";
        data.Position = new FVector(4, 5, 6);
        obj.Data = data;
        FPropertyProbeData read = obj.Data;
        check(read.Count == 7 && read.Label == data.Label && read.Position.X == 4 && read.Position.Z == 6,
            "generated custom struct property marshals owned FString and FVector");
        FPropertyProbeData changed = new() { Count = 3, Label = "before" };
        FPropertyProbeData result = obj.ExchangeData(data, ref changed, out FPropertyProbeData output);
        check(changed.Count == 10 && changed.Label == "before:ref" && output.Count == 7 && output.Label == "payload: 中文:out"
            && result.Count == 14 && result.Label == data.Label && result.Position.Y == 5 && data.Count == 7,
            "generated custom struct input/ref/out/return round trip");
    }
}
