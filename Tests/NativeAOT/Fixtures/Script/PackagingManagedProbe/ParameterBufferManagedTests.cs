using UnrealSharp.Attributes;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.PackageTargetFixture;

namespace PackagingManagedProbe;

[UClass]
public partial class UParameterBufferManagedTests : UObject
{
    [UProperty]
    public partial string Report { get; set; }

    [UFunction(FunctionFlags.BlueprintCallable)]
    public bool RunTests(AActor First, AActor Second)
    {
        List<string> checks = new();
        void Check(bool condition, string name)
        {
            checks.Add($"{(condition ? "PASS" : "FAIL")} {name}");
            if (!condition)
            {
                throw new InvalidOperationException(name);
            }
        }

        try
        {
            UParameterBufferExample example = NewObject<UParameterBufferExample>();
            FVector vector = new(1, 2, 3);
            FHitResult hit = new()
            {
                Location = new FVector(4, 5, 6),
                ImpactPoint = new FVector(7, 8, 9),
                Distance = 12.5f,
                Item = 4,
                BlockingHit = true
            };

            example.Foo(7, "input: 中文", vector, hit, First);
            Check(example.LastCount == 7 && example.LastMessage == "input: 中文", "Foo int/string inputs");
            Check(Equal(example.LastVector, vector), "Foo FVector by value");
            Check(Equal(example.LastHit.Location, hit.Location) && example.LastHit.Item == 4
                && example.LastHit.Distance == 12.5f && example.LastHit.BlockingHit,
                "Foo const FHitResult fields and bitfield");
            Check(example.StoredActor == First, "AActor* input stored in TObjectPtr member");
            Check(Equal(vector, new FVector(1, 2, 3)) && Equal(hit.Location, new FVector(4, 5, 6)),
                "Foo leaves caller input values unchanged");

            // These locals have no input values: the generated wrapper must not read them.
            example.MakeOutputs(7, "output: 中文", First, out int count, out string message,
                out FVector outputVector, out FHitResult outputHit, out AActor outputActor);
            Check(count == 17 && message == "output: 中文|out", "out int/string");
            Check(Equal(outputVector, new FVector(7, 8, 9)), "out FVector");
            Check(Equal(outputHit.Location, outputVector) && outputHit.Distance == 12.5f
                && outputHit.Item == 4 && outputHit.BlockingHit, "out FHitResult fields and bitfield");
            Check(outputActor == First, "out Actor identity");

            count = 7;
            message = "ref: 中文";
            AActor actor = First;
            example.ModifyRefs(ref count, ref message, ref vector, ref hit, ref actor, Second);
            Check(count == 17 && message == "ref: 中文|native", "ref int/string read old values and write back");
            Check(Equal(vector, new FVector(11, 22, 33)), "ref FVector read and write back");
            Check(Equal(hit.Location, new FVector(5, 7, 9)) && hit.Distance == 17.5f
                && hit.Item == 5 && !hit.BlockingHit, "ref FHitResult read and write back");
            Check(Equal(hit.ImpactPoint, new FVector(7, 8, 9)), "ref FHitResult preserves untouched fields");
            Check(actor == Second, "ref Actor replacement");

            int input = 3;
            int value = 4;
            int result = example.Mixed(input, ref value, out int output);
            Check(input == 3 && value == 7 && output == 14 && result == 21, "mixed input/ref/out/return");

            AActor first = First;
            AActor second = Second;
            example.SwapActors(ref first, ref second);
            Check(first == Second && second == First, "two ref Actor values swap");
            first = null!;
            example.SwapActors(ref first, ref second);
            Check(first == First && second == null, "ref Actor null round trip");

            example.Foo(0, string.Empty, new FVector(), new FHitResult(), null!);
            checks.Add($"DETAIL cleared Actor null={example.StoredActor == null}; FString length={example.LastMessage.Length}; value=[{example.LastMessage}]");
            Check(example.StoredActor == null, "null input and TObjectPtr clear");
            Check(example.LastMessage == string.Empty, "empty FString after nonempty assignment");

            foreach (string prefix in new[] { string.Empty, "Unicode: 中文", new string('x', 8192) })
            {
                example.MakeOutputs(0, prefix, null!, out count, out message,
                    out outputVector, out outputHit, out outputActor);
                Check(message == prefix + "|out" && outputActor == null,
                    $"out string/null Actor length={prefix.Length}");
            }
            Report = string.Join("\n", checks) + $"\nTOTAL {checks.Count} PASS\n";
            return true;
        }
        catch (Exception error)
        {
            Report = string.Join("\n", checks) + $"\nERROR {error}\n";
            return false;
        }
    }

    private static bool Equal(FVector a, FVector b) =>
        Math.Abs(a.X - b.X) < 0.0001 && Math.Abs(a.Y - b.Y) < 0.0001 && Math.Abs(a.Z - b.Z) < 0.0001;
}
