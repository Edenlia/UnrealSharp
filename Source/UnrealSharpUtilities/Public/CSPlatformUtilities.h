#pragma once

#include "CoreMinimal.h"
#include "Misc/Optional.h"

namespace UnrealSharp::PlatformUtilities
{
	inline constexpr bool SupportsJit = !PLATFORM_IOS;

	UNREALSHARPUTILITIES_API FString GetNativeAotLibraryPath(const FString& UserWorkingDirectory);
	UNREALSHARPUTILITIES_API void* LoadNativeAotLibrary(const FString& LibraryPath);
	UNREALSHARPUTILITIES_API void* GetNativeAotEntryPoint(void* Library);
	UNREALSHARPUTILITIES_API TOptional<FString> GetNativeAotLoadError();
}
