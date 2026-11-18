#pragma once

#include "CoreMinimal.h"

#if UNREALSHARP_WITH_NATIVEAOT_HOST

#include "Runtime/CSRuntimeHost.h"

/**
 * Calls InitializeAotRuntime in a statically linked Wasm image or a bundled shared library. There is no separate runtime to
 * start: the library already contains the precompiled managed code, GC and type system. Every assembly listed in
 * the load-order manifests is linked into that library, so nothing can be loaded from disk or unloaded later.
 */
class FCSNativeAotRuntimeHost final : public FCSRuntimeHost
{
public:
#if defined(PLATFORM_WASM) && PLATFORM_WASM
	FCSNativeAotRuntimeHost() = default;
#else
	explicit FCSNativeAotRuntimeHost(FString InLibraryPath);
#endif

	// FCSRuntimeHost interface
	virtual ECSRuntimeKind GetKind() const override { return ECSRuntimeKind::NativeAot; }
	virtual FCSRuntimeCapabilities GetCapabilities() const override;

protected:
	virtual FInitializeUnrealSharp StartRuntime() override;
	// End of FCSRuntimeHost interface

#if !PLATFORM_WASM
private:
	FString LibraryPath;
	// NativeAOT does not support unloading. Keep this library loaded until process exit.
	void* Library = nullptr;
#endif
};

#endif // UNREALSHARP_WITH_NATIVEAOT_HOST
