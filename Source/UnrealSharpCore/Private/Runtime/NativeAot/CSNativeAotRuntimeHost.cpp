#include "Runtime/NativeAot/CSNativeAotRuntimeHost.h"

#if UNREALSHARP_WITH_NATIVEAOT_HOST

#include "CSPlatformUtilities.h"
#include "UnrealSharpCore.h"
#include "Logging/StructuredLog.h"

using namespace UnrealSharp;

#if defined(PLATFORM_WASM) && PLATFORM_WASM
// The interpreter links the same host without an AOT app archive. The AOT link roots this symbol explicitly.
extern "C" void InitializeAotRuntime(const UTF8CHAR*, FCSManagedPluginCallbacks*, const void*, FCSManagedCallbacks*,
                                     FCSInitializationResult*) __attribute__((weak));
#else
FCSNativeAotRuntimeHost::FCSNativeAotRuntimeHost(FString InLibraryPath)
	: LibraryPath(MoveTemp(InLibraryPath))
{
}

#endif

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
#if defined(PLATFORM_WASM) && PLATFORM_WASM
	if (!IsInGameThread() || !InitializeAotRuntime)
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "Wasm NativeAOT requires the game thread and a linked InitializeAotRuntime export.");
		return nullptr;
	}
	return &InitializeAotRuntime;
#else
	Library = PlatformUtilities::LoadBundledLibrary(LibraryPath);
	if (!Library)
	{
		const TOptional<FString> LoadError = PlatformUtilities::GetBundledLibraryLoadError();
		if (LoadError.IsSet())
		{
			UE_LOGFMT(LogUnrealSharp, Error, "NativeAOT dlopen error: {0}", LoadError.GetValue());
		}
		UE_LOGFMT(LogUnrealSharp, Fatal, "Failed to load NativeAOT library: {0}", LibraryPath);
	}

	FInitializeUnrealSharp InitializeUnrealSharp = reinterpret_cast<FInitializeUnrealSharp>(
		PlatformUtilities::GetBundledLibraryExport(Library, TEXT("InitializeAotRuntime")));
	if (!InitializeUnrealSharp)
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "NativeAOT library is missing InitializeAotRuntime: {0}", LibraryPath);
	}
	UE_LOGFMT(LogUnrealSharp, Display, "Using NativeAOT runtime: {0}", LibraryPath);

	return InitializeUnrealSharp;
#endif
}

#endif // UNREALSHARP_WITH_NATIVEAOT_HOST
