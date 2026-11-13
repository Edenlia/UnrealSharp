#include "UnrealSharpCore.h"
#include "CoreMinimal.h"
#include "CSManager.h"
#include "CSDialogUtilities.h"
#include "CSDotnetUtilties.h"
#include "Logging/StructuredLog.h"
#include "Properties/CSPropertyGeneratorManager.h"
#include "Modules/ModuleManager.h"
#include "Runtime/CSRuntimeHost.h"
#include "Runtime/CSRuntimeHostFactory.h"

#define LOCTEXT_NAMESPACE "FUnrealSharpCoreModule"

DEFINE_LOG_CATEGORY(LogUnrealSharp);

FUnrealSharpCoreModule::FUnrealSharpCoreModule() = default;
FUnrealSharpCoreModule::~FUnrealSharpCoreModule() = default;

ECSRuntimeKind FUnrealSharpCoreModule::GetRuntimeKind() const
{
	check(RuntimeHost);
	return RuntimeHost->GetKind();
}

FCSRuntimeCapabilities FUnrealSharpCoreModule::GetRuntimeCapabilities() const
{
	check(RuntimeHost);
	return RuntimeHost->GetCapabilities();
}

void FUnrealSharpCoreModule::StartupModule()
{
#if WITH_EDITOR
	while (!UnrealSharp::DotNetUtilities::VerifyCSharpEnvironment() || !UnrealSharp::DotNetUtilities::BuildUserSolution())
	{
		if (UnrealSharp::Dialogs::IsHeadless())
		{
			UE_LOGFMT(LogUnrealSharp, Error, "UnrealSharp could not be initialized, see the errors above. Exiting.");
			FPlatformMisc::RequestExitWithStatus(true, 1);
			return;
		}

		if (IsEngineExitRequested())
		{
			return;
		}
	}
#endif
	
	RuntimeHost = UnrealSharp::RuntimeHostFactory::CreateRuntimeHost();
	if (!RuntimeHost || !RuntimeHost->Initialize())
	{
		return;
	}
	
	UCSManager::Get().Initialize();
}

void FUnrealSharpCoreModule::ShutdownModule()
{
	FCSPropertyGeneratorManager::Shutdown();
}

#undef LOCTEXT_NAMESPACE
	
IMPLEMENT_MODULE(FUnrealSharpCoreModule, UnrealSharpCore)
