#include "UnrealSharpCore.h"
#include "CoreMinimal.h"
#include "CSManager.h"
#include "CSDialogUtilities.h"
#include "CSDotnetUtilties.h"
#include "Logging/StructuredLog.h"
#include "Misc/ScopedSlowTask.h"
#include "Properties/CSPropertyGeneratorManager.h"
#include "Modules/ModuleManager.h"

#define LOCTEXT_NAMESPACE "FUnrealSharpCoreModule"

DEFINE_LOG_CATEGORY(LogUnrealSharp);

void FUnrealSharpCoreModule::StartupModule()
{
#if WITH_EDITOR
	const bool bHeadless = UnrealSharp::Dialogs::IsHeadless();
	for (;;)
	{
		FScopedSlowTask Progress(4, LOCTEXT("CSharpStartup", "Starting UnrealSharp"), !bHeadless);
		Progress.Visibility = ESlowTaskVisibility::Important;
		auto BeginStage = [&Progress](const FText& Message)
		{
			Progress.EnterProgressFrame(1, Message);
			Progress.ForceRefresh();
			UE_LOGFMT(LogUnrealSharp, Display, "{0}", Message.ToString());
		};

		BeginStage(LOCTEXT("VerifyCSharpEnvironment", "[1/4] UnrealSharp: Checking the C# environment"));
		if (UnrealSharp::DotNetUtilities::VerifyCSharpEnvironment())
		{
			BeginStage(LOCTEXT("BuildUserSolution", "[2/4] UnrealSharp: Checking and building C# projects"));
			if (UnrealSharp::DotNetUtilities::BuildUserSolution())
			{
				BeginStage(LOCTEXT("InitializeManagedRuntime", "[3/4] UnrealSharp: Starting .NET"));
				if (!DotNetRuntimeHost.InitializeManagedRuntime())
				{
					return;
				}

				BeginStage(LOCTEXT("InitializeManager", "[4/4] UnrealSharp: Loading C# assemblies"));
				UCSManager::Get().Initialize();
				Progress.EnterProgressFrame(0);
				return;
			}
		}

		if (bHeadless)
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
#else
	if (!DotNetRuntimeHost.InitializeManagedRuntime())
	{
		return;
	}
	
	UCSManager::Get().Initialize();
#endif
}

void FUnrealSharpCoreModule::ShutdownModule()
{
	FCSPropertyGeneratorManager::Shutdown();
}

#undef LOCTEXT_NAMESPACE
	
IMPLEMENT_MODULE(FUnrealSharpCoreModule, UnrealSharpCore)
