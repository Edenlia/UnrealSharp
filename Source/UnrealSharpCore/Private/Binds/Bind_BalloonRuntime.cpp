#include "CSBindsRegistry.h"
#include "CSManager.h"
#include "Engine/GameInstance.h"
#include "Engine/World.h"
#include "GameFramework/GameModeBase.h"
#include "GameFramework/Pawn.h"
#include "GameFramework/PlayerController.h"
#include "Kismet/GameplayStatics.h"
#include "ProfilingDebugging/CpuProfilerTrace.h"

// Candidate destination: UnrealSharpCore/Private/Binds. No Balloon dependency.
// Context is explicit: generated WorldContext metadata otherwise substitutes
// UCSManager's current context, which need not be this session during callbacks.
DECLARE_UNREALSHARP_BINDER(Bind_BalloonRuntime)
{
    void* Managed(UObject* Object)
    {
        return IsValid(Object) ? UCSManager::Get().FindManagedObject(Object) : nullptr;
    }
    void* GameInstance(UObject* Context)
    {
        return IsInGameThread() && IsValid(Context) ? Managed(UGameplayStatics::GetGameInstance(Context)) : nullptr;
    }
    void* GameMode(UObject* Context)
    {
        return IsInGameThread() && IsValid(Context) ? Managed(UGameplayStatics::GetGameMode(Context)) : nullptr;
    }
    void* PlayerPawn(UObject* Context, int32 Index)
    {
        return IsInGameThread() && IsValid(Context) ? Managed(UGameplayStatics::GetPlayerPawn(Context, Index)) : nullptr;
    }
    void* PlayerController(UObject* Context, int32 Index)
    {
        return IsInGameThread() && IsValid(Context) ? Managed(UGameplayStatics::GetPlayerController(Context, Index)) : nullptr;
    }

#if CPUPROFILERTRACE_ENABLED
    struct FCpuScope { int64 Id; bool bEnabled; };
    TArray<FCpuScope> CpuScopes;
    int64 NextCpuScopeId = 0;
#endif

    // Direct calli binding, never a UFunction: the ProcessEvent trace scope
    // would otherwise close while the child scope returned here remained open.
    int64 BeginCpuScope(const uint8* Utf8Name)
    {
        if (!IsInGameThread() || Utf8Name == nullptr) return -1;
        const FString Name = FString(UTF8_TO_TCHAR(reinterpret_cast<const char*>(Utf8Name))).TrimStartAndEnd();
        if (Name.IsEmpty() || Name.Len() > 1024) return -1;
#if CPUPROFILERTRACE_ENABLED
        const FCpuScope Scope { ++NextCpuScopeId, TRACE_CPUPROFILER_EVENT_MANUAL_IS_ENABLED() };
        if (Scope.bEnabled) FCpuProfilerTrace::OutputBeginDynamicEvent(*Name, __FILE__, __LINE__);
        CpuScopes.Add(Scope);
        return Scope.Id;
#else
        return 0;
#endif
    }
    uint8 EndCpuScope(int64 Id)
    {
        if (Id == 0) return 1;
        if (!IsInGameThread()) return 0;
#if CPUPROFILERTRACE_ENABLED
        if (CpuScopes.IsEmpty() || CpuScopes.Last().Id != Id) return 0;
        if (CpuScopes.Last().bEnabled) FCpuProfilerTrace::OutputEndEvent();
        CpuScopes.Pop(EAllowShrinking::No);
        return 1;
#else
        return 0;
#endif
    }
    BIND_UNREALSHARP_FUNCTION(GameInstance)
    BIND_UNREALSHARP_FUNCTION(GameMode)
    BIND_UNREALSHARP_FUNCTION(PlayerPawn)
    BIND_UNREALSHARP_FUNCTION(PlayerController)
    BIND_UNREALSHARP_FUNCTION(BeginCpuScope)
    BIND_UNREALSHARP_FUNCTION(EndCpuScope)
}
