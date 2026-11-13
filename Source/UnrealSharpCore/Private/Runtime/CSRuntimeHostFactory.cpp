#include "Runtime/CSRuntimeHostFactory.h"

#include "CSPathsUtilities.h"
#include "CSPlatformUtilities.h"
#include "UnrealSharpCore.h"
#include "Logging/StructuredLog.h"
#include "Misc/Paths.h"
#include "Runtime/CSRuntimeManifest.h"

#if UNREALSHARP_WITH_HOSTFXR_HOST
#include "Runtime/HostFxr/CSHostFxrRuntimeHost.h"
#endif
#if UNREALSHARP_WITH_NATIVEAOT_HOST
#include "Runtime/NativeAot/CSNativeAotRuntimeHost.h"
#endif
#if UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST
#include "Runtime/CoreClrEmbedded/CSEmbeddedCoreClrRuntimeHost.h"
#endif

using namespace UnrealSharp;

namespace
{
	TUniquePtr<FCSRuntimeHost> CreateHostFxrHost(const FString& ManagedDirectory)
	{
#if UNREALSHARP_WITH_HOSTFXR_HOST
		return MakeUnique<FCSHostFxrRuntimeHost>();
#else
		UE_LOGFMT(LogUnrealSharp, Fatal, "This platform cannot start CoreCLR through hostfxr. Package a runtime that supports it into {0}", ManagedDirectory);
		return nullptr;
#endif
	}

	TUniquePtr<FCSRuntimeHost> CreateHostForManifest(const FCSRuntimeManifest& Manifest, const FString& ManagedDirectory)
	{
		switch (Manifest.Kind)
		{
		case ECSRuntimeKind::CoreClrHostFxr:
			return CreateHostFxrHost(ManagedDirectory);
		case ECSRuntimeKind::NativeAot:
#if UNREALSHARP_WITH_NATIVEAOT_HOST
		{
			// Packages that only carry the legacy marker do not name their library.
			const FString LibraryReference = Manifest.Library.IsEmpty()
				? PlatformUtilities::GetLegacyNativeAotLibraryReference()
				: Manifest.Library;
			return MakeUnique<FCSNativeAotRuntimeHost>(PlatformUtilities::ResolveBundledLibraryPath(ManagedDirectory, LibraryReference));
		}
#else
			break;
#endif
		case ECSRuntimeKind::CoreClrEmbedded:
#if UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST
			return MakeUnique<FCSEmbeddedCoreClrRuntimeHost>(Manifest, ManagedDirectory);
#else
			break;
#endif
		}

		UE_LOGFMT(LogUnrealSharp, Fatal, "Runtime kind {0} from {1} is not available in this build.", LexToString(Manifest.Kind), Manifest.SourcePath);
		return nullptr;
	}
}

TUniquePtr<FCSRuntimeHost> RuntimeHostFactory::CreateRuntimeHost()
{
	const FString ManagedDirectory = FPaths::ConvertRelativePathToFull(Paths::GetUserAssemblyDirectory());

	TUniquePtr<FCSRuntimeHost> Host;
#if WITH_EDITOR
	// The Editor builds and hot reloads managed code, which only the hostfxr runtime supports.
	Host = CreateHostFxrHost(ManagedDirectory);
	const TCHAR* Source = TEXT("Editor");
#else
	const TOptional<FCSRuntimeManifest> Manifest = FCSRuntimeManifest::Load(ManagedDirectory);
	Host = Manifest.IsSet() ? CreateHostForManifest(Manifest.GetValue(), ManagedDirectory) : CreateHostFxrHost(ManagedDirectory);
	const FString Source = Manifest.IsSet() ? Manifest->SourcePath : FString(TEXT("no runtime manifest"));
#endif

	if (Host)
	{
		UE_LOGFMT(LogUnrealSharp, Display, "Runtime host: {0} ({1})", LexToString(Host->GetKind()), Source);
	}
	return Host;
}
