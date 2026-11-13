#include "CSPlatformUtilities.h"

#if PLATFORM_ANDROID

FString UnrealSharp::PlatformUtilities::GetNativeAotLibraryPath(const FString&)
{
	// Resolve the image through the application's native library directory/APK.
	return TEXT("libUnrealSharp.AOT.so");
}

#endif
