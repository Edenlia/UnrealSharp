#include "UnrealSharpCore.h"
#include "CoreMinimal.h"
#include "CSManager.h"
#include "CSDialogUtilities.h"
#include "CSDotnetUtilties.h"
#include "Properties/CSPropertyGeneratorManager.h"
#include "Modules/ModuleManager.h"

#define LOCTEXT_NAMESPACE "FUnrealSharpCoreModule"

DEFINE_LOG_CATEGORY(LogUnrealSharp);

void FUnrealSharpCoreModule::StartupModule()
{
#if WITH_EDITOR
	while (!UnrealSharp::DotNetUtilities::VerifyCSharpEnvironment())
	{
		if (!UnrealSharp::Dialogs::CanShowEditorUI())
		{
			UE_LOG(LogUnrealSharp, Error, TEXT("UnrealSharp initialization failed. No interactive dialog is available; exiting with status 1."));
			FPlatformMisc::RequestExitWithStatus(true, 1);
			return;
		}

		if (IsEngineExitRequested())
		{
			return;
		}
	}

	// Compile errors must not block the Editor; they are shown in the C# Compile Log once the Editor is up.
	if (!UnrealSharp::DotNetUtilities::BuildUserSolution())
	{
		if (!UnrealSharp::Dialogs::CanShowEditorUI())
		{
			UE_LOG(LogUnrealSharp, Error, TEXT("C# startup build failed in unattended mode; exiting with status 1."));
			FPlatformMisc::RequestExitWithStatus(true, 1);
			return;
		}
		UE_LOG(LogUnrealSharp, Warning, TEXT("C# build failed; continuing to start with the last successfully built assemblies. See the C# Compile Log for errors."));
	}
#endif
	
	if (!DotNetRuntimeHost.InitializeManagedRuntime())
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
