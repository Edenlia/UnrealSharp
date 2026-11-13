#include "DotNet/CSDotNetRuntimeHost.h"

#include "UnrealSharpCore.h"
#include "CSPathsUtilities.h"
#include "CSPlatformUtilities.h"
#include "Runtime/HostFxr/CSHostFxrRuntimeHost.h"
#include "Runtime/NativeAot/CSNativeAotRuntimeHost.h"
#include "Misc/Paths.h"
#include "Logging/StructuredLog.h"

using namespace UnrealSharp;

FCSDotNetRuntimeHost::FCSDotNetRuntimeHost() = default;
FCSDotNetRuntimeHost::~FCSDotNetRuntimeHost() = default;

bool FCSDotNetRuntimeHost::IsNativeAOT() const
{
	return Host && Host->GetKind() == ECSRuntimeKind::NativeAot;
}

bool FCSDotNetRuntimeHost::InitializeManagedRuntime()
{
	const FString UserWorkingDirectory = FPaths::ConvertRelativePathToFull(Paths::GetUserAssemblyDirectory());
#if !WITH_EDITOR
	if (FPaths::FileExists(UserWorkingDirectory / TEXT("UnrealSharp.NativeAOT")))
	{
		Host = MakeUnique<FCSNativeAotRuntimeHost>(PlatformUtilities::GetNativeAotLibraryPath(UserWorkingDirectory));
		return Host->Initialize();
	}
#endif

	if constexpr (!PlatformUtilities::SupportsJit)
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "This platform requires a NativeAOT package in {0}", UserWorkingDirectory);
		return false;
	}
	else
	{
		Host = MakeUnique<FCSHostFxrRuntimeHost>();
		return Host->Initialize();
	}
}
