#pragma once

#include "CoreMinimal.h"
#include "Templates/UniquePtr.h"

class FCSRuntimeHost;

namespace UnrealSharp::RuntimeHostFactory
{
	/**
	 * Creates the runtime host for this process. The Editor always uses hostfxr; packaged builds follow the
	 * runtime manifest written during packaging, and fall back to hostfxr when no manifest is present on platforms
	 * that can run it. Returns null only after logging a fatal error.
	 */
	TUniquePtr<FCSRuntimeHost> CreateRuntimeHost();
}
