#include "CoreMinimal.h"
#include "HAL/PlatformMisc.h"
#include "Misc/CommandLine.h"
#include "Misc/CoreDelegates.h"
#include "Misc/Parse.h"
#include "UObject/StructOnScope.h"
#include "UObject/UnrealType.h"
#include "UObject/GarbageCollection.h"

namespace
{
struct FProbeCall
{
    UObject* Object;
    UFunction* Function;
    FStructOnScope Parameters;

    FProbeCall(UObject* InObject, const TCHAR* Name)
        : Object(InObject), Function(InObject->FindFunctionChecked(Name)), Parameters(Function) {}

    void SetInt(const TCHAR* Name, int32 Value)
    {
        CastFieldChecked<FIntProperty>(Function->FindPropertyByName(Name))->SetPropertyValue_InContainer(Parameters.GetStructMemory(), Value);
    }

    int32 GetInt(const TCHAR* Name) const
    {
        return CastFieldChecked<FIntProperty>(Function->FindPropertyByName(Name))->GetPropertyValue_InContainer(Parameters.GetStructMemory());
    }

    void SetString(const TCHAR* Name, const FString& Value)
    {
        CastFieldChecked<FStrProperty>(Function->FindPropertyByName(Name))->SetPropertyValue_InContainer(Parameters.GetStructMemory(), Value);
    }

    FString GetString(const TCHAR* Name) const
    {
        return CastFieldChecked<FStrProperty>(Function->FindPropertyByName(Name))->GetPropertyValue_InContainer(Parameters.GetStructMemory());
    }

    void Invoke() { Object->ProcessEvent(Function, Parameters.GetStructMemory()); }
};

bool RunMatrixProbe()
{
    UClass* Class = FindObject<UClass>(nullptr, TEXT("/Script/UnrealSharp.NativeAotMatrix_C"));
    UClass* TickClass = FindObject<UClass>(nullptr, TEXT("/Script/UnrealSharp.NativeAotTickProbe_C"));
    UClass* DerivedClass = FindObject<UClass>(nullptr, TEXT("/Script/UnrealSharp.NativeAotDerived_C"));
    if (!Class || !TickClass || !DerivedClass) return false;
    UObject* Object = NewObject<UObject>(GetTransientPackage(), Class);
    Object->AddToRoot();
    bool bPassed;
    {
        FProbeCall Matrix(Object, TEXT("RunMatrix")); Matrix.Invoke();
        const int32 Mask = Matrix.GetInt(TEXT("ReturnValue"));
        bPassed = Mask == 1023;
        FProbeCall Content(Object, TEXT("CheckPublishedContent")); Content.Invoke();
        bPassed &= Content.GetInt(TEXT("ReturnValue")) == 42;
        UE_LOG(LogTemp, Display, TEXT("NATIVEAOT_PUBLISHED_CONTENT native_dependency=%d expected=42"), Content.GetInt(TEXT("ReturnValue")));
        FProbeCall StaticAdd(Object, TEXT("StaticAdd"));
        StaticAdd.SetInt(TEXT("lhs"), 19); StaticAdd.SetInt(TEXT("rhs"), 23); StaticAdd.Invoke();
        bPassed &= StaticAdd.GetInt(TEXT("ReturnValue")) == 42;
        UObject* Derived = NewObject<UObject>(Object, DerivedClass);
        FProbeCall Inherited(Derived, TEXT("Add"));
        Inherited.SetInt(TEXT("lhs"), 13); Inherited.SetInt(TEXT("rhs"), 29); Inherited.Invoke();
        bPassed &= Inherited.GetInt(TEXT("ReturnValue")) == 42;
        UObject* TickObject = NewObject<UObject>(Object, TickClass);
        FProbeCall Tick(TickObject, TEXT("ReceiveTick"));
        CastFieldChecked<FFloatProperty>(Tick.Function->FindPropertyByName(TEXT("DeltaSeconds")))
            ->SetPropertyValue_InContainer(Tick.Parameters.GetStructMemory(), 1.25f);
        Tick.Invoke();
        const int32 TickValue = CastFieldChecked<FIntProperty>(TickClass->FindPropertyByName(TEXT("TickValue")))
            ->GetPropertyValue_InContainer(TickObject);
        bPassed &= TickValue == 125;
        UE_LOG(LogTemp, Display, TEXT("NATIVEAOT_MATRIX mask=%d expected=1023 static=%d inherited=%d override=%d"),
            Mask, StaticAdd.GetInt(TEXT("ReturnValue")), Inherited.GetInt(TEXT("ReturnValue")), TickValue);
        if (FParse::Param(FCommandLine::Get(), TEXT("NativeAotExceptionProbe")))
        {
            UClass* SettingsClass = FindObject<UClass>(nullptr, TEXT("/Script/UnrealSharpCore.CSUnrealSharpSettings"));
            UObject* Settings = SettingsClass->GetDefaultObject();
            FBoolProperty* CrashOnException = CastFieldChecked<FBoolProperty>(SettingsClass->FindPropertyByName(TEXT("bCrashOnException")));
            const bool bOriginal = CrashOnException->GetPropertyValue_InContainer(Settings);
            CrashOnException->SetPropertyValue_InContainer(Settings, false);
            FProbeCall Throw(Object, TEXT("ThrowExpected")); Throw.Invoke();
            CrashOnException->SetPropertyValue_InContainer(Settings, bOriginal);
            StaticAdd.Invoke();
            bPassed &= StaticAdd.GetInt(TEXT("ReturnValue")) == 42;
            UE_LOG(LogTemp, Display, TEXT("NATIVEAOT_EXCEPTION returned_control=1 subsequent_call=%d"), StaticAdd.GetInt(TEXT("ReturnValue")));
        }
    }
    FProbeCall CountBefore(Class->GetDefaultObject(), TEXT("GetDisposedCount")); CountBefore.Invoke();
    const int32 Before = CountBefore.GetInt(TEXT("ReturnValue"));
    TWeakObjectPtr<UObject> WeakObject(Object);
    Object->RemoveFromRoot();
    Object = nullptr;
    CollectGarbage(RF_NoFlags, true);
    FProbeCall CountAfter(Class->GetDefaultObject(), TEXT("GetDisposedCount")); CountAfter.Invoke();
    const int32 After = CountAfter.GetInt(TEXT("ReturnValue"));
    bPassed &= !WeakObject.IsValid() && After > Before;
    UE_LOG(LogTemp, Display, TEXT("NATIVEAOT_NATIVE_GC collected=%d disposed_before=%d disposed_after=%d"), !WeakObject.IsValid(), Before, After);
    return bPassed;
}

void RunNativeAotProbe()
{
    if (!FParse::Param(FCommandLine::Get(), TEXT("NativeAotProbe"))) return;
    UClass* Class = FindObject<UClass>(nullptr, TEXT("/Script/UnrealSharp.NativeAotProbe_C"));
    if (!Class)
    {
        UE_LOG(LogTemp, Error, TEXT("NATIVEAOT_GAME_SMOKE FAIL: managed class missing"));
        FPlatformMisc::RequestExitWithStatus(false, 41);
        return;
    }
    UObject* Object = NewObject<UObject>(GetTransientPackage(), Class);
    Object->AddToRoot();
    const FIntProperty* StoredValue = CastFieldChecked<FIntProperty>(Class->FindPropertyByName(TEXT("StoredValue")));
    bool bPassed = StoredValue->GetPropertyValue_InContainer(Object) == 11;

    FProbeCall Add(Object, TEXT("Add"));
    Add.SetInt(TEXT("lhs"), 13);
    Add.SetInt(TEXT("rhs"), 29);
    Add.Invoke();
    bPassed &= Add.GetInt(TEXT("ReturnValue")) == 42;

    FProbeCall Roundtrip(Object, TEXT("NativePropertyRoundtrip"));
    Roundtrip.SetInt(TEXT("value"), 123);
    Roundtrip.Invoke();
    bPassed &= Roundtrip.GetInt(TEXT("ReturnValue")) == 123 && StoredValue->GetPropertyValue_InContainer(Object) == 123;

    FProbeCall Adjust(Object, TEXT("Adjust"));
    Adjust.SetInt(TEXT("value"), 7);
    Adjust.Invoke();
    bPassed &= Adjust.GetInt(TEXT("value")) == 10 && Adjust.GetInt(TEXT("doubled")) == 20;

    const FString Unicode = TEXT("AOT \u4e2d\u6587 caf\u00e9");
    FProbeCall Echo(Object, TEXT("Echo"));
    Echo.SetString(TEXT("text"), Unicode);
    Echo.Invoke();
    bPassed &= Echo.GetString(TEXT("ReturnValue")) == Unicode;

    FProbeCall Mode(Object, TEXT("RuntimeMode"));
    Mode.Invoke();
    FString ExpectedMode;
    FParse::Value(FCommandLine::Get(), TEXT("NativeAotExpected="), ExpectedMode);
    const FString ActualMode = Mode.GetString(TEXT("ReturnValue"));
    bPassed &= !ExpectedMode.IsEmpty() && ActualMode == ExpectedMode;
    if (FParse::Param(FCommandLine::Get(), TEXT("NativeAotMatrix"))) bPassed &= RunMatrixProbe();
    if (FParse::Param(FCommandLine::Get(), TEXT("NativeAotBenchmark")))
    {
        constexpr int32 Iterations = 50000;
        for (int32 Index = 0; Index < 1000; ++Index) { Add.Invoke(); Roundtrip.Invoke(); }
        int64 AddChecksum = 0, PropertyChecksum = 0;
        double Start = FPlatformTime::Seconds();
        for (int32 Index = 0; Index < Iterations; ++Index) { Add.Invoke(); AddChecksum += Add.GetInt(TEXT("ReturnValue")); }
        double AddMs = (FPlatformTime::Seconds() - Start) * 1000;
        Start = FPlatformTime::Seconds();
        for (int32 Index = 0; Index < Iterations; ++Index) { Roundtrip.Invoke(); PropertyChecksum += Roundtrip.GetInt(TEXT("ReturnValue")); }
        double PropertyMs = (FPlatformTime::Seconds() - Start) * 1000;
        bPassed &= AddChecksum == int64(Iterations) * 42 && PropertyChecksum == int64(Iterations) * 123;
        UE_LOG(LogTemp, Display, TEXT("NATIVEAOT_BENCH mode=%s iterations=%d add_ms=%.6f property_ms=%.6f add_checksum=%lld property_checksum=%lld"),
            *ActualMode, Iterations, AddMs, PropertyMs, AddChecksum, PropertyChecksum);
    }
    UE_LOG(LogTemp, Display, TEXT("NATIVEAOT_GAME_SMOKE %s class=%s scalar=%d property=%d ref=%d out=%d unicode=%d mode=%s expected=%s"),
        bPassed ? TEXT("PASS") : TEXT("FAIL"), *Class->GetPathName(), Add.GetInt(TEXT("ReturnValue")),
        Roundtrip.GetInt(TEXT("ReturnValue")), Adjust.GetInt(TEXT("value")), Adjust.GetInt(TEXT("doubled")),
        Echo.GetString(TEXT("ReturnValue")) == Unicode, *ActualMode, *ExpectedMode);
    Object->RemoveFromRoot();
    FPlatformMisc::RequestExitWithStatus(false, bPassed ? 0 : 42);
}

struct FRegisterNativeAotProbe
{
    FRegisterNativeAotProbe() { FCoreDelegates::GetOnPostEngineInit().AddStatic(&RunNativeAotProbe); }
} RegisterNativeAotProbe;
}
