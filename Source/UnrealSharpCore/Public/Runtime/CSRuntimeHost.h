#pragma once

#include "CoreMinimal.h"
#include "Runtime/CSRuntimeTypes.h"

/**
 * Starts one kind of managed runtime and hands control to UnrealSharp's managed entry point.
 *
 * A .NET runtime can be started only once per process and cannot be restarted after shutdown, so a host is
 * initialized exactly once and stays alive for the rest of the process. Hot reload happens inside the runtime
 * through collectible AssemblyLoadContexts, never by restarting the host.
 */
class UNREALSHARPCORE_API FCSRuntimeHost
{
public:
	virtual ~FCSRuntimeHost() = default;

	virtual ECSRuntimeKind GetKind() const = 0;
	virtual FCSRuntimeCapabilities GetCapabilities() const = 0;

	/** Starts the runtime, calls InitializeUnrealSharp and reports whether managed initialization succeeded. */
	bool Initialize();

	bool IsInitialized() const { return bInitialized; }

protected:
	/** Starts the runtime if needed and resolves the managed InitializeUnrealSharp entry point. */
	virtual FInitializeUnrealSharp StartRuntime() = 0;

private:
	bool bInitialized = false;
};
