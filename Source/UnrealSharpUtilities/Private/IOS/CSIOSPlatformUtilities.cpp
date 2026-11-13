#include "CSPlatformUtilities.h"

#if PLATFORM_IOS

#include "HAL/PlatformProcess.h"
#include "HAL/FileManager.h"
#include "Misc/Paths.h"
#include <dlfcn.h>

FString UnrealSharp::PlatformUtilities::GetManagedWorkingDirectory(const FString& UserWorkingDirectory)
{
	// Managed file APIs need the physical bundle path rather than the iOS virtual path.
	return IFileManager::Get().ConvertToAbsolutePathForExternalAppForRead(*UserWorkingDirectory);
}

FString UnrealSharp::PlatformUtilities::ResolveBundledLibraryPath(const FString& ManagedDirectory, const FString& LibraryReference)
{
	// App Store builds may only load dynamic libraries from signed frameworks inside the app bundle.
	return FPaths::GetPath(FPlatformProcess::ExecutablePath()) / TEXT("Frameworks") / LibraryReference;
}

FString UnrealSharp::PlatformUtilities::GetLegacyNativeAotLibraryReference()
{
	return TEXT("UnrealSharpAOT.framework/UnrealSharpAOT");
}

void* UnrealSharp::PlatformUtilities::LoadBundledLibrary(const FString& LibraryPath)
{
	// iOS inherits the unimplemented generic FPlatformProcess DLL operations.
	return dlopen(TCHAR_TO_UTF8(*LibraryPath), RTLD_NOW | RTLD_LOCAL);
}

void* UnrealSharp::PlatformUtilities::GetBundledLibraryExport(void* Library, const TCHAR* ExportName)
{
	return dlsym(Library, TCHAR_TO_UTF8(ExportName));
}

TOptional<FString> UnrealSharp::PlatformUtilities::GetBundledLibraryLoadError()
{
	const char* Error = dlerror();
	if (!Error)
	{
		return {};
	}
	return FString(UTF8_TO_TCHAR(Error));
}

#endif
