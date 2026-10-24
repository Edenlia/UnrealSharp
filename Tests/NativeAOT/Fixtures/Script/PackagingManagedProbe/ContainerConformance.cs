using UnrealSharp.Attributes;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;

namespace PackagingManagedProbe;

[UClass]
public partial class UContainerProbe : UObject
{
    [UProperty] public partial IList<int> Numbers { get; set; }
    [UProperty] public partial ISet<FName> Names { get; set; }
    [UProperty] public partial IDictionary<string, int> Counts { get; set; }
    [UProperty] public partial IList<FPropertyProbeData> Records { get; set; }
}

public static class ContainerConformance
{
    public static void Run(Action<bool, string> check)
    {
        UContainerProbe obj = UObject.NewObject<UContainerProbe>();
        obj.Numbers.Add(3);
        obj.Numbers.Add(7);
        obj.Numbers.Insert(1, 5);
        obj.Numbers[0] = 1;
        obj.Numbers.RemoveAt(2);
        check(obj.Numbers.SequenceEqual(new[] { 1, 5 }), "native-backed array add/insert/index/remove/enumerate");
        bool replacementRejected = false;
        try { obj.Numbers = new List<int> { -4, 9, 12 }; }
        catch (NotSupportedException) { replacementRejected = true; }
        check(replacementRejected && obj.Numbers.SequenceEqual(new[] { 1, 5 }),
            "whole-container setter rejects replacement without mutation");
        obj.Numbers.Clear();
        foreach (int value in new[] { -4, 9, 12 }) obj.Numbers.Add(value);
        check(obj.Numbers.SequenceEqual(new[] { -4, 9, 12 }), "native-backed array clears and repopulates");

        FName first = new("FirstName");
        FName second = new("SecondName");
        bool added = obj.Names.Add(first);
        bool duplicate = obj.Names.Add(first);
        obj.Names.Add(second);
        obj.Names.Remove(first);
        check(added && !duplicate && obj.Names.Count == 1 && obj.Names.Contains(second),
            "native-backed FName set deduplicates and removes");

        obj.Counts.Add("中文", 4);
        obj.Counts["second"] = 8;
        obj.Counts["中文"] = 9;
        check(obj.Counts.Count == 2 && obj.Counts.TryGetValue("中文", out int count) && count == 9,
            "native-backed string map adds/updates/looks up Unicode keys");
        bool removed = obj.Counts.Remove("second");
        check(removed && !obj.Counts.ContainsKey("second") && obj.Counts.Single().Value == 9,
            "native-backed map removes and enumerates");

        obj.Records.Add(new FPropertyProbeData { Count = 2, Label = "array: 中文", Position = new FVector(4, 5, 6) });
        obj.Records.Add(new FPropertyProbeData { Count = 3, Label = "second" });
        obj.Records.RemoveAt(1);
        FPropertyProbeData record = obj.Records[0];
        check(record.Count == 2 && record.Label == "array: 中文" && record.Position.Z == 6 && obj.Records.Count == 1,
            "native-backed custom struct array owns nested FString values");

        obj.Numbers.Clear();
        obj.Names.Clear();
        obj.Counts.Clear();
        obj.Records.Clear();
        check(obj.Numbers.Count == 0 && obj.Names.Count == 0 && obj.Counts.Count == 0 && obj.Records.Count == 0,
            "native-backed array/set/map/struct-array clear");
    }
}
