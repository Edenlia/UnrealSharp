#include "DotNet/CSDotNetRuntimeHost.h"

#include "CSBindsRegistry.h"
#include "UnrealSharpCore.h"
#include "CSDotnetUtilties.h"
#include "CSManagedPluginCallbacks.h"
#include "CSPathsUtilities.h"
#include "CSPlatformUtilities.h"
#include "Runtime/HostFxr/CSHostFxrRuntimeHost.h"
#include "HAL/PlatformProcess.h"
#include "Misc/Paths.h"
#include "Logging/StructuredLog.h"

using namespace UnrealSharp;

FCSDotNetRuntimeHost::FCSDotNetRuntimeHost() = default;
FCSDotNetRuntimeHost::~FCSDotNetRuntimeHost() = default;

bool FCSDotNetRuntimeHost::InitializeManagedRuntime()
{
	const FString UserWorkingDirectory = FPaths::ConvertRelativePathToFull(Paths::GetUserAssemblyDirectory());
	FInitializeUnrealSharp InitializeUnrealSharp = nullptr;
#if !WITH_EDITOR
	const FString AotLibraryPath = PlatformUtilities::GetNativeAotLibraryPath(UserWorkingDirectory);
	if (FPaths::FileExists(UserWorkingDirectory / TEXT("UnrealSharp.NativeAOT")))
	{
		NativeAotLibrary = PlatformUtilities::LoadNativeAotLibrary(AotLibraryPath);
		if (!NativeAotLibrary)
		{
			const TOptional<FString> LoadError = PlatformUtilities::GetNativeAotLoadError();
			if (LoadError.IsSet())
			{
				UE_LOGFMT(LogUnrealSharp, Error, "NativeAOT dlopen error: {0}", LoadError.GetValue());
			}
			UE_LOGFMT(LogUnrealSharp, Fatal, "Failed to load NativeAOT library: {0}", AotLibraryPath);
		}

		InitializeUnrealSharp = reinterpret_cast<FInitializeUnrealSharp>(
			PlatformUtilities::GetNativeAotEntryPoint(NativeAotLibrary));
		if (!InitializeUnrealSharp)
		{
			UE_LOGFMT(LogUnrealSharp, Fatal, "NativeAOT library is missing InitializeAotRuntime: {0}", AotLibraryPath);
		}
		UE_LOGFMT(LogUnrealSharp, Display, "Using NativeAOT runtime: {0}", AotLibraryPath);
	}
#endif

	if (!InitializeUnrealSharp)
	{
		if constexpr (!PlatformUtilities::SupportsJit)
		{
			UE_LOGFMT(LogUnrealSharp, Fatal, "This platform requires a NativeAOT package in {0}", UserWorkingDirectory);
			return false;
		}
		else
		{
			JitHost = MakeUnique<FCSHostFxrRuntimeHost>();
			return JitHost->Initialize();
		}
	}

	const FTCHARToUTF8 WorkingDirectoryUtf8(*PlatformUtilities::GetManagedWorkingDirectory(UserWorkingDirectory));

	FCSInitializationResult InitializationResult;

	InitializeUnrealSharp(
		(const UTF8CHAR*)WorkingDirectoryUtf8.Get(),
		&GetManagedPluginCallbacks(),
		(const void*)&FCSBindsRegistry::GetBoundFunction,
		&GetManagedCallbacks(),
		&InitializationResult);

	if (!InitializationResult.bSuccess)
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "Failed to initialize UnrealSharp! Exception:\n{0}", FString(StringCast<TCHAR>(InitializationResult.Message)));
	}

#if !(UE_BUILD_SHIPPING)
	if (FParse::Param(FCommandLine::Get(), TEXT("-waitformanageddebugger")))
	{
		while (!FPlatformMisc::IsDebuggerPresent());
	}
#endif

	return true;
}
