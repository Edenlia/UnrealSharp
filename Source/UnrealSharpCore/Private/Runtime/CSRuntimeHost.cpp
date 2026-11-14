#include "Runtime/CSRuntimeHost.h"

#include "CSBindsRegistry.h"
#include "CSManagedCallbacksCache.h"
#include "CSManagedPluginCallbacks.h"
#include "CSPathsUtilities.h"
#include "CSPlatformUtilities.h"
#include "UnrealSharpCore.h"
#include "Logging/StructuredLog.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"

const TCHAR* LexToString(ECSRuntimeKind Kind)
{
	switch (Kind)
	{
	case ECSRuntimeKind::CoreClrHostFxr: return TEXT("CoreClrHostFxr");
	case ECSRuntimeKind::CoreClrEmbedded: return TEXT("CoreClrEmbedded");
	case ECSRuntimeKind::NativeAot: return TEXT("NativeAot");
	}
	return TEXT("Unknown");
}

bool FCSRuntimeHost::Initialize()
{
	check(!bInitialized);

	FInitializeUnrealSharp InitializeUnrealSharp = StartRuntime();
	if (!InitializeUnrealSharp)
	{
		return false;
	}

	const FString UserWorkingDirectory = FPaths::ConvertRelativePathToFull(UnrealSharp::Paths::GetUserAssemblyDirectory());
	const FTCHARToUTF8 WorkingDirectoryUtf8(*UnrealSharp::PlatformUtilities::GetManagedWorkingDirectory(UserWorkingDirectory));

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

	bInitialized = true;
	return true;
}
