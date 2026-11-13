#include "Runtime/NativeAot/CSNativeAotRuntimeHost.h"

#include "CSPlatformUtilities.h"
#include "UnrealSharpCore.h"
#include "Logging/StructuredLog.h"

using namespace UnrealSharp;

FCSNativeAotRuntimeHost::FCSNativeAotRuntimeHost(FString InLibraryPath)
	: LibraryPath(MoveTemp(InLibraryPath))
{
}

FCSRuntimeCapabilities FCSNativeAotRuntimeHost::GetCapabilities() const
{
	FCSRuntimeCapabilities Capabilities;
	Capabilities.bCanLoadAssembliesFromPath = false;
	Capabilities.bSupportsCollectibleLoadContexts = false;
	Capabilities.bSupportsHotReload = false;
	Capabilities.bAssembliesArePrelinked = true;
	return Capabilities;
}

FInitializeUnrealSharp FCSNativeAotRuntimeHost::StartRuntime()
{
	Library = PlatformUtilities::LoadNativeAotLibrary(LibraryPath);
	if (!Library)
	{
		const TOptional<FString> LoadError = PlatformUtilities::GetNativeAotLoadError();
		if (LoadError.IsSet())
		{
			UE_LOGFMT(LogUnrealSharp, Error, "NativeAOT dlopen error: {0}", LoadError.GetValue());
		}
		UE_LOGFMT(LogUnrealSharp, Fatal, "Failed to load NativeAOT library: {0}", LibraryPath);
	}

	FInitializeUnrealSharp InitializeUnrealSharp = reinterpret_cast<FInitializeUnrealSharp>(
		PlatformUtilities::GetNativeAotEntryPoint(Library));
	if (!InitializeUnrealSharp)
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "NativeAOT library is missing InitializeAotRuntime: {0}", LibraryPath);
	}
	UE_LOGFMT(LogUnrealSharp, Display, "Using NativeAOT runtime: {0}", LibraryPath);

	return InitializeUnrealSharp;
}
