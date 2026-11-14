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
FString UnrealSharp::PlatformUtilities::ResolveBundledLibraryPath(const FString& ManagedDirectory, const FString& LibraryReference)
{
	return ManagedDirectory / LibraryReference;
}

FString UnrealSharp::PlatformUtilities::GetLegacyNativeAotLibraryReference()
{
	return FString(FApp::GetProjectName()) + TEXT(".AOT.") + FPlatformProcess::GetModuleExtension();
}
#endif

void* UnrealSharp::PlatformUtilities::LoadBundledLibrary(const FString& LibraryPath)
{
	return FPlatformProcess::GetDllHandle(*LibraryPath);
}

void* UnrealSharp::PlatformUtilities::GetBundledLibraryExport(void* Library, const TCHAR* ExportName)
{
	return FPlatformProcess::GetDllExport(Library, ExportName);
}

TOptional<FString> UnrealSharp::PlatformUtilities::GetBundledLibraryLoadError()
{
	return {};
}

#endif
