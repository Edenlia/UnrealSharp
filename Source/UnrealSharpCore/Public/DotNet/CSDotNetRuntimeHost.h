#pragma once

#include "CoreMinimal.h"

#include "HAL/PlatformProcess.h"
#include "Runtime/CSRuntimeTypes.h"
#include "Templates/UniquePtr.h"

class FCSRuntimeHost;

class FCSDotNetRuntimeHost
{
public:
	FCSDotNetRuntimeHost();
	~FCSDotNetRuntimeHost();

	bool InitializeManagedRuntime();
	bool IsNativeAOT() const { return NativeAotLibrary != nullptr; }

private:
	TUniquePtr<FCSRuntimeHost> JitHost;
	// NativeAOT does not support unloading. Keep this library loaded until process exit.
	void* NativeAotLibrary = nullptr;
};
