#pragma once

#include "CoreMinimal.h"

#if UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST

#include "Runtime/CSRuntimeHost.h"
#include "Runtime/CSRuntimeManifest.h"

/**
 * Starts CoreCLR directly through coreclr_initialize, without hostfxr or hostpolicy. This is how CoreCLR runs on
 * platforms that ship no hostfxr, such as iOS in .NET 11, where code runs from platform-native ReadyToRun
 * composite images and the CoreCLR interpreter executes anything that was not precompiled.
 *
 * The packaged runtime manifest supplies what hostfxr would otherwise compute: the libcoreclr library, runtime
 * properties baked from runtimeconfig.json, the libraries p/invokes bind to, and the ReadyToRun composite images.
 * Every managed assembly in the managed directory goes on the trusted platform assembly list.
 */
class FCSEmbeddedCoreClrRuntimeHost final : public FCSRuntimeHost
{
public:
	FCSEmbeddedCoreClrRuntimeHost(FCSRuntimeManifest InManifest, FString InManagedDirectory);

	// FCSRuntimeHost interface
	virtual ECSRuntimeKind GetKind() const override { return ECSRuntimeKind::CoreClrEmbedded; }
	virtual FCSRuntimeCapabilities GetCapabilities() const override;

protected:
	virtual FInitializeUnrealSharp StartRuntime() override;
	// End of FCSRuntimeHost interface

private:
	FCSRuntimeManifest Manifest;
	/** Managed directory as the Unreal file system sees it. */
	FString ManagedDirectory;
};

#endif // UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST
