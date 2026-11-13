#include "DotNet/CSDotNetRuntimeHost.h"

#if PLATFORM_IOS

#include "CSPlatformUtilities.h"
#include "Misc/Paths.h"

FInitializeUnrealSharp ResolveIOSReadyToRun(const FString& ManagedDirectory);

FInitializeUnrealSharp FCSDotNetRuntimeHost::ResolvePlatformEntryPoint(const FString& UserWorkingDirectory)
{
	if (!FPaths::FileExists(UserWorkingDirectory / TEXT("UnrealSharp.ReadyToRun")))
	{
		return nullptr;
	}
	return ResolveIOSReadyToRun(UnrealSharp::PlatformUtilities::GetManagedWorkingDirectory(UserWorkingDirectory));
}

#endif
