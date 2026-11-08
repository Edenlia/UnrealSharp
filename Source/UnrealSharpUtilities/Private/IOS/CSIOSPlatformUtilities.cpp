#include "CSPlatformUtilities.h"

#if PLATFORM_IOS

#include "HAL/PlatformProcess.h"
#include "Misc/Paths.h"
#include <dlfcn.h>

FString UnrealSharp::PlatformUtilities::GetNativeAotLibraryPath(const FString& UserWorkingDirectory)
{
	return FPaths::GetPath(FPlatformProcess::ExecutablePath()) /
		TEXT("Frameworks/UnrealSharpAOT.framework/UnrealSharpAOT");
}

void* UnrealSharp::PlatformUtilities::LoadNativeAotLibrary(const FString& LibraryPath)
{
	// iOS inherits the unimplemented generic FPlatformProcess DLL operations.
	return dlopen(TCHAR_TO_UTF8(*LibraryPath), RTLD_NOW | RTLD_LOCAL);
}

void* UnrealSharp::PlatformUtilities::GetNativeAotEntryPoint(void* Library)
{
	return dlsym(Library, "InitializeAotRuntime");
}

TOptional<FString> UnrealSharp::PlatformUtilities::GetNativeAotLoadError()
{
	return FString(UTF8_TO_TCHAR(dlerror()));
}

#endif
