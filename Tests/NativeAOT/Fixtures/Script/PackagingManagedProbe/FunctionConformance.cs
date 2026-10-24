using UnrealSharp.Attributes;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;
using UnrealSharp.Interop;

namespace PackagingManagedProbe;

[UClass]
public partial class UFunctionBaseProbe : UObject
{
    [UFunction(FunctionFlags.BlueprintCallable)]
    public static int StaticAdd(int input) => input + 19;

    [UFunction(FunctionFlags.BlueprintEvent)]
    public partial FName Exchange(bool enabled, FName name, ref int value, out string text);

    public virtual partial FName Exchange_Implementation(bool enabled, FName name, ref int value, out string text)
    {
        value += enabled ? 3 : -3;
        text = name.ToString() + ":base";
        return name;
    }

    [UFunction(FunctionFlags.BlueprintEvent)]
    public partial int NoArguments();

    public partial int NoArguments_Implementation() => 47;
}

[UClass]
public partial class UFunctionDerivedProbe : UFunctionBaseProbe
{
    public override FName Exchange_Implementation(bool enabled, FName name, ref int value, out string text)
    {
        base.Exchange_Implementation(enabled, name, ref value, out text);
        value *= 2;
        text += ":derived";
        return new FName("ReturnedName");
    }
}

public static class FunctionConformance
{
    private static unsafe int InvokeStaticThroughUnreal(UObject instance, int input)
    {
        IntPtr function = Bind_UClass.CallGetNativeFunctionFromInstanceAndName(instance.NativeObject, nameof(UFunctionBaseProbe.StaticAdd));
        if (function == IntPtr.Zero)
        {
            throw new InvalidOperationException("Static UFunction was not registered");
        }
        int size = Bind_UFunction.CallGetNativeFunctionParamsSize(function);
        int inputOffset = Bind_FProperty.CallGetPropertyOffsetFromName(function, "input");
        int returnOffset = Bind_FProperty.CallGetPropertyOffsetFromName(function, "ReturnValue");
        if (inputOffset < 0 || returnOffset < 0 || inputOffset + sizeof(int) > size || returnOffset + sizeof(int) > size)
        {
            throw new InvalidOperationException("Unexpected static UFunction parameter layout");
        }
        byte* buffer = stackalloc byte[size];
        new Span<byte>(buffer, size).Clear();
        *(int*)(buffer + inputOffset) = input;
        Bind_UObject.CallInvokeNativeStaticFunction(instance.Class.NativeObject, function, (IntPtr)buffer, (IntPtr)(buffer + returnOffset));
        return *(int*)(buffer + returnOffset);
    }

    public static void Run(Action<bool, string> check)
    {
        UFunctionBaseProbe baseObject = UObject.NewObject<UFunctionBaseProbe>();
        check(InvokeStaticThroughUnreal(baseObject, 23) == 42, "generated static function through Unreal reflection and CDO");
        int value = 5;
        FName name = new("InputName");
        FName result = baseObject.Exchange(true, name, ref value, out string text);
        check(result == name && value == 8 && text == "InputName:base", "generated bool/name/ref/out/return round trip");
        result = baseObject.Exchange(false, name, ref value, out text);
        check(result == name && value == 5, "generated false bool parameter");

        UFunctionBaseProbe derived = UObject.NewObject<UFunctionDerivedProbe>();
        value = 7;
        result = derived.Exchange(true, name, ref value, out text);
        check(result == new FName("ReturnedName") && value == 20 && text == "InputName:base:derived",
            "generated inherited function dispatches virtual implementation");
        check(derived.NoArguments() == 47, "generated inherited no-argument return");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        check(derived.NoArguments() == 47, "generated callback remains callable after managed GC");
    }
}
