#include "CSPlatformUtilities.h"

#if PLATFORM_ANDROID

FString UnrealSharp::PlatformUtilities::ResolveBundledLibraryPath(const FString&, const FString& LibraryReference)
{
	// Resolve the image through the application's native library directory/APK.
	return LibraryReference;
}

FString UnrealSharp::PlatformUtilities::GetLegacyNativeAotLibraryReference()
{
	return TEXT("libUnrealSharp.AOT.so");
}

#endif
