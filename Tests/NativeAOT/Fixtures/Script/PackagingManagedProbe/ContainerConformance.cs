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
    [UProperty] public partial int FunctionCalls { get; set; }

    [UFunction(FunctionFlags.BlueprintEvent)]
    public partial IList<int> Transform(string prefix, IList<int> input, ref IList<string> labels, out IDictionary<string, int> counts);

    public partial IList<int> Transform_Implementation(string prefix, IList<int> input, ref IList<string> labels, out IDictionary<string, int> counts)
    {
        FunctionCalls++;
        labels = labels.Select(label => prefix + label).ToList();
        counts = new Dictionary<string, int> { [prefix] = input.Sum() };
        return input.Select(value => value * 2).ToList();
    }
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

        IList<int> input = new List<int> { 2, 3 };
        IList<string> labels = new List<string> { "label: 中文" };
        IList<int> output = obj.Transform("prefix:", input, ref labels, out IDictionary<string, int> counts);
        check(input.SequenceEqual(new[] { 2, 3 }) && output.SequenceEqual(new[] { 4, 6 })
            && labels.Single() == "prefix:label: 中文" && counts["prefix:"] == 5,
            "generated array input/ref array/out map/array return round trip");
        for (int i = 0; i < 32; i++)
        {
            IList<string> temporary = new List<string> { new string('x', 1024) };
            obj.Transform("reuse:", input, ref temporary, out _);
        }
        check(output.SequenceEqual(new[] { 4, 6 }) && labels.Single() == "prefix:label: 中文" && counts["prefix:"] == 5,
            "returned container copies survive native parameter destruction and stack reuse");

        int calls = obj.FunctionCalls;
        bool rejected = false;
        try { obj.Transform(new string('x', 8192), new FailingList(), ref labels, out _); }
        catch (InvalidOperationException error) when (error.Message == "Expected marshaling failure") { rejected = true; }
        IList<string> recovered = new List<string> { "ok" };
        IList<int> recoveredOutput = obj.Transform("after:", input, ref recovered, out _);
        check(rejected && obj.FunctionCalls == calls + 1 && recoveredOutput.Sum() == 10 && recovered.Single() == "after:ok",
            "managed marshaling failure unwinds native parameter lifetime and next call succeeds");
    }

    // ToNative copies an ICollection before allocating its native array. The preceding
    // FString has already been marshaled, so this exercises the generated finally path.
    private sealed class FailingList : IList<int>
    {
        public int Count => throw new InvalidOperationException("Expected marshaling failure");
        public bool IsReadOnly => true;
        public int this[int index] { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public void Add(int item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(int item) => throw new NotSupportedException();
        public void CopyTo(int[] array, int arrayIndex) => throw new NotSupportedException();
        public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public int IndexOf(int item) => throw new NotSupportedException();
        public void Insert(int index, int item) => throw new NotSupportedException();
        public bool Remove(int item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }
}
