using UnrealSharp;
using UnrealSharp.Attributes;
using UnrealSharp.CoreUObject;

namespace PackagingManagedProbe;

[USingleDelegate]
public delegate void FConformanceSingle(int value, string text);

[UMultiDelegate]
public delegate void FConformanceMulti(int value, string text);

[UClass]
public partial class UDelegateProbe : UObject
{
    [UProperty] public partial TDelegate<FConformanceSingle> Single { get; set; }
    [UProperty] public partial TMulticastDelegate<FConformanceMulti> Multi { get; set; }
    [UProperty] public partial int Total { get; set; }
    [UProperty] public partial int Calls { get; set; }
    [UProperty] public partial string LastText { get; set; }

    [UFunction]
    public void Receive(int value, string text)
    {
        Total += value;
        Calls++;
        LastText = text;
    }

    [UFunction(FunctionFlags.BlueprintEvent)]
    public partial bool InvokeSingle(TDelegate<FConformanceSingle> callback, int value, string text);

    public partial bool InvokeSingle_Implementation(TDelegate<FConformanceSingle> callback, int value, string text)
    {
        if (!callback.IsBound) return false;
        callback.InnerDelegate.Invoke(value, text);
        return true;
    }
}

public static class DelegateConformance
{
    public static void Run(Action<bool, string> check)
    {
        UDelegateProbe first = UObject.NewObject<UDelegateProbe>();
        UDelegateProbe second = UObject.NewObject<UDelegateProbe>();
        TDelegate<FConformanceSingle> single = new();
        single.Add(first.Receive);
        Console.WriteLine("NATIVEAOT DELEGATE assigning native property");
        first.Single = single;
        Console.WriteLine("NATIVEAOT DELEGATE native property assigned");
        check(first.Single.IsBound && first.Single.Contains(first.Receive), "generated single delegate wrapper binds a UFunction");
        bool invoked = first.InvokeSingle(first.Single, 7, "single: 中文");
        Console.WriteLine("NATIVEAOT DELEGATE parameter invocation returned");
        check(invoked && first.Total == 7 && first.Calls == 1 && first.LastText == "single: 中文",
            "single delegate parameter round trip invokes native-bound managed callback");
        check(first.InvokeSingle(first.Single, 0, string.Empty) && first.Total == 7 && first.Calls == 2 && first.LastText == string.Empty,
            "repeated delegate parameter call reinitializes native storage");
        single.Remove(first.Receive);
        first.Single = new TDelegate<FConformanceSingle>();
        check(!single.IsBound && !first.InvokeSingle(first.Single, 99, "unbound") && first.Total == 7,
            "single delegate removal and empty parameter preserve callback state");

        first.Multi.Add(first.Receive);
        first.Multi.Add(second.Receive);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        first.Multi.Invoke(11, "multi: 中文");
        check(first.Total == 18 && second.Total == 11 && first.Calls == 3 && second.Calls == 1
            && first.LastText == "multi: 中文" && second.LastText == first.LastText,
            "native multicast invokes two generated callbacks after managed GC");
        first.Multi.Remove(second.Receive);
        first.Multi.Invoke(2, "remaining");
        check(first.Total == 20 && second.Total == 11 && first.Multi.Contains(first.Receive) && !first.Multi.Contains(second.Receive),
            "native multicast removal preserves remaining binding");
        first.Multi.Clear();
        check(!first.Multi.IsBound, "native multicast clears all bindings");
    }
}
