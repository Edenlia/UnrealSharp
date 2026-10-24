using UnrealSharp.Attributes;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;

namespace PackagingManagedProbe;

[UInterface]
public partial interface IConformanceProbe
{
    [UFunction(FunctionFlags.BlueprintCallable)]
    int Compute(int value, string text);

    [UFunction(FunctionFlags.BlueprintCallable)]
    void Bump();

    [UFunction(FunctionFlags.BlueprintEvent)]
    void Mark(FName label);
}

[UClass]
public partial class UInterfaceProbe : UObject, IConformanceProbe
{
    [UProperty] public partial int Total { get; set; }
    [UProperty] public partial FName Label { get; set; }

    public int Compute(int value, string text)
    {
        Total = value + text.Length;
        return Total;
    }

    public void Bump() => Total++;

    public partial void Mark(FName label);
    public partial void Mark_Implementation(FName label) => Label = label;

    [UFunction(FunctionFlags.BlueprintEvent)]
    public partial IConformanceProbe EchoInterface(IConformanceProbe value);
    public partial IConformanceProbe EchoInterface_Implementation(IConformanceProbe value) => value;
}

public static class InterfaceConformance
{
    public static void Run(Action<bool, string> check)
    {
        UInterfaceProbe obj = UObject.NewObject<UInterfaceProbe>();
        IConformanceProbe? view = obj.AsInterface<IConformanceProbe>();
        check(view != null && !ReferenceEquals(view, obj) && view.AsUObject() == obj && IConformanceProbe.Implements(obj),
            "generated native interface wrapper preserves UObject identity");
        check(view!.Compute(9, "中文") == 11 && obj.Total == 11,
            "interface wrapper dispatches callable function with string input and return");
        view.Bump();
        check(obj.Total == 12, "interface wrapper dispatches callable function without parameters");
        view.Mark(new FName("InterfaceEvent"));
        check(obj.Label == new FName("InterfaceEvent"), "interface wrapper dispatches generated event implementation");
        IConformanceProbe echoed = obj.EchoInterface(view);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        check(echoed.AsUObject() == obj && echoed.Compute(4, "abc") == 7,
            "generated interface parameter/return remains callable after managed GC");
    }
}
