#include "CSWasmCoreClrRuntimeHost.h"

#if PLATFORM_WASM && UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST
#include "CSWasmCoreClrKernel.h"
#include "UnrealSharpCore.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformTLS.h"
#include "HAL/PlatformProcess.h"
#include "HAL/PlatformMisc.h"
#include "Logging/StructuredLog.h"
#include "Misc/Paths.h"

namespace
{
std::string Utf8(const FString& Text)
{
    const FTCHARToUTF8 Value(*Text);
    return std::string(Value.Get(), Value.Length());
}
}

FCSWasmCoreClrRuntimeHost::FCSWasmCoreClrRuntimeHost(FCSRuntimeManifest InManifest, FString InManagedDirectory)
    : Manifest(MoveTemp(InManifest)), ManagedDirectory(MoveTemp(InManagedDirectory))
{
}

FCSRuntimeCapabilities FCSWasmCoreClrRuntimeHost::GetCapabilities() const
{
    FCSRuntimeCapabilities Capabilities;
    Capabilities.bCanLoadAssembliesFromPath = true;
    return Capabilities; // no collectible/hot reload/prelinked-image claims
}

FInitializeUnrealSharp FCSWasmCoreClrRuntimeHost::StartRuntime()
{
    if (!IsInGameThread() || OwnerThreadId != 0)
    {
        UE_LOGFMT(LogUnrealSharp, Fatal, "Wasm CoreCLR must start once on the UE game/owner thread.");
        return nullptr;
    }
    if (!Manifest.Library.IsEmpty() || !Manifest.NativeLibraries.IsEmpty() || !Manifest.ReadyToRunImages.IsEmpty())
    {
        UE_LOGFMT(LogUnrealSharp, Fatal, "Wasm static CoreCLR cannot consume a dynamic native/R2R manifest: {0}", Manifest.SourcePath);
        return nullptr;
    }
    OwnerThreadId = FPlatformTLS::GetCurrentThreadId();
    UnrealSharp::WasmCoreClr::FStartArguments Arguments;
    Arguments.ManagedDirectory = Utf8(ManagedDirectory);
    Arguments.ExecutablePath = Utf8(FPlatformProcess::ExecutablePath());
    TArray<FString> Assemblies;
    IFileManager::Get().FindFiles(Assemblies, *(ManagedDirectory / TEXT("*.dll")), true, false);
    Assemblies.Sort();
    for (const FString& Name : Assemblies) Arguments.AssemblyFiles.push_back(Utf8(Name));
    for (const TPair<FString, FString>& Property : Manifest.Properties)
        Arguments.Properties.emplace_back(Utf8(Property.Key), Utf8(Property.Value));

    void* Entry = UnrealSharp::WasmCoreClr::Start(Arguments);
    if (!Entry)
    {
        const FString ErrorMessage(UTF8_TO_TCHAR(UnrealSharp::WasmCoreClr::GetError()));
        UE_LOGFMT(LogUnrealSharp, Fatal, "Wasm CoreCLR failed: {0}", ErrorMessage);
        return nullptr;
    }
    return reinterpret_cast<FInitializeUnrealSharp>(Entry);
}

void FCSWasmCoreClrRuntimeHost::Shutdown()
{
    if (OwnerThreadId == 0) return;
    if (!IsInGameThread() || FPlatformTLS::GetCurrentThreadId() != OwnerThreadId)
    {
        UE_LOGFMT(LogUnrealSharp, Fatal, "Wasm CoreCLR shutdown reached a different UE worker; no cross-thread CLR call is permitted.");
        return;
    }
    const int Result = UnrealSharp::WasmCoreClr::Shutdown();
    if (Result != 0)
    {
        const FString ErrorMessage(UTF8_TO_TCHAR(UnrealSharp::WasmCoreClr::GetError()));
        UE_LOGFMT(LogUnrealSharp, Error, "Wasm CoreCLR shutdown failed ({0}): {1}", Result, ErrorMessage);
        // Kernel already published Closed/Failed and retained the original result.
        // The public platform API takes uint8: never truncate a nonzero result to zero.
        const uint8 PlatformExitCode = static_cast<uint8>(Result > 0 && Result <= 255 ? Result : 1);
        FPlatformMisc::RequestExitWithStatus(true, PlatformExitCode, TEXT("UnrealSharp Wasm CoreCLR shutdown"));
    }
}
#endif
