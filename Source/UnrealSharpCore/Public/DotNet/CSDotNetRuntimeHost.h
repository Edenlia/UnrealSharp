#pragma once

#include "CoreMinimal.h"

#include "Runtime/CSRuntimeTypes.h"
#include "Templates/UniquePtr.h"

class FCSRuntimeHost;

class FCSDotNetRuntimeHost
{
public:
	FCSDotNetRuntimeHost();
	~FCSDotNetRuntimeHost();

	bool InitializeManagedRuntime();
	bool IsNativeAOT() const;

private:
	TUniquePtr<FCSRuntimeHost> Host;
};
