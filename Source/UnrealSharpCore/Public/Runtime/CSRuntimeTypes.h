#pragma once

#include "CoreMinimal.h"
#include "CSInteropTypeTraits.h"

struct FCSManagedCallbacks;
struct FCSManagedPluginCallbacks;

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
