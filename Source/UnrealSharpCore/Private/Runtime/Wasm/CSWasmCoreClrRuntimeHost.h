#pragma once

#if defined(PLATFORM_WASM) && PLATFORM_WASM && UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST
#include "Runtime/CSRuntimeHost.h"
#include "Runtime/CSRuntimeManifest.h"

/** Candidate only: same CoreClrEmbedded kind, genuine static hosting APIs and loose PE files. */
class FCSWasmCoreClrRuntimeHost final : public FCSRuntimeHost
{
public:
    FCSWasmCoreClrRuntimeHost(FCSRuntimeManifest InManifest, FString InManagedDirectory);
    virtual ECSRuntimeKind GetKind() const override { return ECSRuntimeKind::CoreClrEmbedded; }
    virtual FCSRuntimeCapabilities GetCapabilities() const override;
    virtual void Shutdown() override;

protected:
    virtual FInitializeUnrealSharp StartRuntime() override;

private:
    FCSRuntimeManifest Manifest;
    FString ManagedDirectory;
    uint32 OwnerThreadId = 0;
};
#endif
