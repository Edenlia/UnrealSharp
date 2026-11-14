#pragma once

#include "CoreMinimal.h"
#include "CSInteropTypeTraits.h"

struct FCSManagedCallbacks;
struct FCSManagedPluginCallbacks;

/** How managed code is brought into the process. Each kind has its own FCSRuntimeHost implementation. */
enum class ECSRuntimeKind : uint8
{
	/** CoreCLR started through hostfxr (desktop JIT, optionally with ReadyToRun images). */
	CoreClrHostFxr,
	/** CoreCLR started directly through coreclr_initialize, for platforms that ship no hostfxr (iOS). */
	CoreClrEmbedded,
	/** A NativeAOT shared library: managed code is precompiled and there is no separate runtime to start. */
	NativeAot,
};

UNREALSHARPCORE_API const TCHAR* LexToString(ECSRuntimeKind Kind);

/** What the active runtime can do. Callers should test these instead of checking a specific runtime kind. */
struct FCSRuntimeCapabilities
{
	/** New assemblies can be loaded from files at runtime. */
	bool bCanLoadAssembliesFromPath = false;
	/** Assemblies can be loaded into collectible AssemblyLoadContexts and unloaded later. */
	bool bSupportsCollectibleLoadContexts = false;
	/** Managed code can be rebuilt and reloaded while the process runs. */
	bool bSupportsHotReload = false;
	/** Assemblies are already linked into the runtime image; manifest paths are names, not files on disk. */
	bool bAssembliesArePrelinked = false;
};

/**
 * Result of the managed InitializeUnrealSharp entry point. Mirrors UnrealSharp.Plugins.FCSInitializationResult:
 * on failure, managed code writes the exception text into Message as a null-terminated UTF-8 string.
 */
struct FCSInitializationResult
{
	static constexpr int32 MessageCapacity = 4096;

	bool bSuccess = false;
	UTF8CHAR Message[MessageCapacity] = {};
};

static_assert(sizeof(FCSInitializationResult) == 1 + FCSInitializationResult::MessageCapacity, "FCSInitializationResult must match the managed layout.");
CS_ASSERT_INTEROP_SAFE_TYPE(FCSInitializationResult);

using FInitializeUnrealSharp = void (*)(const UTF8CHAR*, FCSManagedPluginCallbacks*, const void*, FCSManagedCallbacks*, FCSInitializationResult*);
CS_ASSERT_INTEROP_SAFE_FUNCTION(FInitializeUnrealSharp);

static_assert(sizeof(TCHAR) == sizeof(char16_t), "TCHAR must be a 16-bit character.");
