#pragma once

#include "CoreMinimal.h"
#include "Runtime/CSRuntimeHost.h"

/**
 * Loads a NativeAOT shared library and calls its exported InitializeAotRuntime. There is no separate runtime to
 * start: the library already contains the precompiled managed code, GC and type system. Every assembly listed in
 * the load-order manifests is linked into that library, so nothing can be loaded from disk or unloaded later.
 */
class FCSNativeAotRuntimeHost final : public FCSRuntimeHost
{
public:
	explicit FCSNativeAotRuntimeHost(FString InLibraryPath);

	// FCSRuntimeHost interface
	virtual ECSRuntimeKind GetKind() const override { return ECSRuntimeKind::NativeAot; }
	virtual FCSRuntimeCapabilities GetCapabilities() const override;

protected:
	virtual FInitializeUnrealSharp StartRuntime() override;
	// End of FCSRuntimeHost interface

private:
	FString LibraryPath;
	// NativeAOT does not support unloading. Keep this library loaded until process exit.
	void* Library = nullptr;
};
