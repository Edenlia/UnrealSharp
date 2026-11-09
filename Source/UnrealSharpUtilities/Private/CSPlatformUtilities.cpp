#include "CSPlatformUtilities.h"

#if !PLATFORM_IOS

#include "HAL/PlatformProcess.h"
#include "Misc/App.h"
#include "Misc/Paths.h"

FString UnrealSharp::PlatformUtilities::GetManagedWorkingDirectory(const FString& UserWorkingDirectory)
{
	return UserWorkingDirectory;
}

#if !PLATFORM_ANDROID
FString UnrealSharp::PlatformUtilities::GetNativeAotLibraryPath(const FString& UserWorkingDirectory)
{
	return UserWorkingDirectory /
		(FString(FApp::GetProjectName()) + TEXT(".AOT.") + FPlatformProcess::GetModuleExtension());
}
#endif

void* UnrealSharp::PlatformUtilities::LoadNativeAotLibrary(const FString& LibraryPath)
{
	return FPlatformProcess::GetDllHandle(*LibraryPath);
}

void* UnrealSharp::PlatformUtilities::GetNativeAotEntryPoint(void* Library)
{
	return FPlatformProcess::GetDllExport(Library, TEXT("InitializeAotRuntime"));
}

TOptional<FString> UnrealSharp::PlatformUtilities::GetNativeAotLoadError()
{
	return {};
}

#endif
