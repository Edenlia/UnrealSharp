#pragma once

#include "CoreMinimal.h"
#include "Misc/Optional.h"

namespace UnrealSharp::PlatformUtilities
{
	inline constexpr bool SupportsJit = !PLATFORM_IOS && !PLATFORM_ANDROID;

	/** Directory passed to managed code as its base directory (the physical path on iOS). */
	UNREALSHARPUTILITIES_API FString GetManagedWorkingDirectory(const FString& UserWorkingDirectory);

	/**
	 * Turns a native library reference written by packaging into a path the platform loader accepts:
	 * relative to the managed directory on desktop, relative to the app's Frameworks directory on iOS, and a
	 * bare file name resolved from the APK's native library directory on Android.
	 */
	UNREALSHARPUTILITIES_API FString ResolveBundledLibraryPath(const FString& ManagedDirectory, const FString& LibraryReference);

	/** Library reference used by NativeAOT packages that predate the runtime manifest. */
	UNREALSHARPUTILITIES_API FString GetLegacyNativeAotLibraryReference();

	UNREALSHARPUTILITIES_API void* LoadBundledLibrary(const FString& LibraryPath);
	UNREALSHARPUTILITIES_API void* GetBundledLibraryExport(void* Library, const TCHAR* ExportName);
	/** Loader error text for the last failed load, where the platform reports one. */
	UNREALSHARPUTILITIES_API TOptional<FString> GetBundledLibraryLoadError();
}
