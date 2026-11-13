#include "Runtime/CSRuntimeHostFactory.h"

#include "CSPathsUtilities.h"
#include "CSPlatformUtilities.h"
#include "UnrealSharpCore.h"
#include "Logging/StructuredLog.h"
#include "Misc/Paths.h"
#include "Runtime/CSRuntimeManifest.h"
#include "Runtime/HostFxr/CSHostFxrRuntimeHost.h"
#include "Runtime/NativeAot/CSNativeAotRuntimeHost.h"

using namespace UnrealSharp;

namespace
{
	TUniquePtr<FCSRuntimeHost> CreateHostFxrHost(const FString& ManagedDirectory)
	{
		if constexpr (!PlatformUtilities::SupportsJit)
		{
			UE_LOGFMT(LogUnrealSharp, Fatal, "This platform cannot start CoreCLR through hostfxr. Package a runtime that supports it into {0}", ManagedDirectory);
			return nullptr;
		}
		else
		{
			return MakeUnique<FCSHostFxrRuntimeHost>();
		}
	}

	TUniquePtr<FCSRuntimeHost> CreateHostForManifest(const FCSRuntimeManifest& Manifest, const FString& ManagedDirectory)
	{
		switch (Manifest.Kind)
		{
		case ECSRuntimeKind::CoreClrHostFxr:
			return CreateHostFxrHost(ManagedDirectory);
		case ECSRuntimeKind::NativeAot:
		{
			// Packages that only carry the legacy marker do not name their library.
			const FString LibraryReference = Manifest.Library.IsEmpty()
				? PlatformUtilities::GetLegacyNativeAotLibraryReference()
				: Manifest.Library;
			return MakeUnique<FCSNativeAotRuntimeHost>(PlatformUtilities::ResolveBundledLibraryPath(ManagedDirectory, LibraryReference));
		}
		case ECSRuntimeKind::CoreClrEmbedded:
			break;
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
