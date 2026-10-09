#include "HotReload/CSHotReloadSubsystem.h"

#include "CSInstallationUtilities.h"
#include "CSCompileLog.h"
#include "CSManager.h"
#include "CSStyle.h"
#include "CompileLog/CSCompileLogTab.h"
#include "Framework/Notifications/NotificationManager.h"
#include "CSUnrealSharpEditorSettings.h"
#include "DirectoryWatcherModule.h"
#include "IDirectoryWatcher.h"
#include "Interfaces/IMainFrameModule.h"
#include "Kismet2/DebuggerCommands.h"
#include "Types/CSEnum.h"
#include "Types/CSScriptStruct.h"
#include "CSPathsUtilities.h"
#include "CSProjectUtilities.h"
#include "HotReload/CSHotReloadUtilities.h"
#include "Kismet2/StructureEditorUtils.h"
#include "Misc/App.h"
#include "Utilities/CSAssemblyUtilities.h"
#include "Utilities/CSEditorUtilities.h"
#include "Widgets/Notifications/SNotificationList.h"

#define LOCTEXT_NAMESPACE "UCSHotReloadSubsystem"

void UCSHotReloadSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);
	UCSManager& Manager = UCSManager::Get();
	Manager.OnNewStructEvent().AddUObject(this, &UCSHotReloadSubsystem::OnStructRebuilt);
	Manager.OnNewClassEvent().AddUObject(this, &UCSHotReloadSubsystem::OnClassRebuilt);
	Manager.OnNewEnumEvent().AddUObject(this, &UCSHotReloadSubsystem::OnEnumRebuilt);
	Manager.OnNewInterfaceEvent().AddUObject(this, &UCSHotReloadSubsystem::OnInterfaceRebuilt);
	
	HotReloadTickHandle = FTickerDelegate::CreateUObject(this, &UCSHotReloadSubsystem::Tick);
	HotReloadTickDelegate = FTSTicker::GetCoreTicker().AddTicker(HotReloadTickHandle);

	FEditorDelegates::ShutdownPIE.AddUObject(this, &UCSHotReloadSubsystem::OnStopPlayingPIE);

	UnrealSharpEditorModule = &FUnrealSharpEditorModule::Get();
	
	FString PathToManagedSolution = UnrealSharp::Paths::GetPathToManagedSolution();
	UnrealSharpEditorModule->GetManagedEditorCallbacks().LoadSolutionAsync(*PathToManagedSolution, (void*)&OnHotReloadReady_Callback);
	
	RefreshDirectoryWatchers();
	
	PauseHotReload(TEXT("Waiting for initial C# load..."));

	// The startup build ran before this subsystem existed; its errors are already in the compile log.
	if (FApp::CanEverRender() && FCSCompileLog::Get().GetCount(ECSCompileLogSeverity::Error) > 0)
	{
		IMainFrameModule& MainFrameModule = IMainFrameModule::Get();
		if (MainFrameModule.IsWindowInitialized())
		{
			FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateWeakLambda(this, [this](float)
			{
				NotifyStartupBuildFailed();
				return false;
			}));
		}
		else
		{
			MainFrameModule.OnMainFrameCreationFinished().AddUObject(this, &UCSHotReloadSubsystem::OnMainFrameCreationFinished);
		}
	}
}

void UCSHotReloadSubsystem::OnMainFrameCreationFinished(TSharedPtr<SWindow> InRootWindow, bool bIsRunningStartupDialog)
{
	IMainFrameModule::Get().OnMainFrameCreationFinished().RemoveAll(this);
	NotifyStartupBuildFailed();
}

void UCSHotReloadSubsystem::NotifyStartupBuildFailed()
{
	if (!FApp::CanEverRender())
	{
		return;
	}

	UnrealSharp::CompileLog::RevealTab();
	ShowCompileFailedNotification(LOCTEXT("StartupBuildFailedNotification", "C# build failed at startup. The Editor is running with the last successfully built C# assemblies."));
}

bool UCSHotReloadSubsystem::ShouldCreateSubsystem(UObject* Outer) const
{
	return !FApp::IsUnattended() && !IsRunningCommandlet() && UnrealSharp::InstallationUtilities::IsDotNetSdkInstalled();
}

void UCSHotReloadSubsystem::Deinitialize()
{
	Super::Deinitialize();
	FTSTicker::GetCoreTicker().RemoveTicker(HotReloadTickDelegate);

	if (IMainFrameModule* MainFrameModule = FModuleManager::GetModulePtr<IMainFrameModule>("MainFrame"))
	{
		MainFrameModule->OnMainFrameCreationFinished().RemoveAll(this);
	}
}

void UCSHotReloadSubsystem::OnHotReloadReady_Callback()
{
	AsyncTask(ENamedThreads::GameThread, []()
	{
		Get()->OnHotReloadReady();
	});
}

void UCSHotReloadSubsystem::OnHotReloadReady()
{
	ResumeHotReload();
	UE_LOGFMT(LogUnrealSharpEditor, Display, "C# Hot Reload is ready.");
}

bool UCSHotReloadSubsystem::HasPendingHotReloadChanges() const
{
	bool bHasPendingChanges = false;
	for (const UCSManagedAssembly* Assembly : PendingModifiedAssemblies)
	{
		if (!Assembly || FCSAssemblyUtilities::IsRuntimeGlueAssembly(Assembly))
		{
			continue;
		}
		
		bHasPendingChanges = true;
		break;
	}
	
	return bHasPendingChanges;
}

void UCSHotReloadSubsystem::PerformHotReload()
{
	TRACE_CPUPROFILER_EVENT_SCOPE(UCSHotReloadSubsystem::PerformHotReload)
	
	if (FPlayWorldCommandCallbacks::IsInPIE() || FPlayWorldCommandCallbacks::IsInSIE())
	{
		UE_LOGFMT(LogUnrealSharpEditor, Verbose, "Cannot perform C# hot reload while in PIE or SIE.");
		return;
	}
	
	if (!HasPendingHotReloadChanges())
	{
		UE_LOGFMT(LogUnrealSharpEditor, Verbose, "No pending C# changes to hot reload.");
		return;
	}
	
	if (IsHotReloading())
	{
		UE_LOGFMT(LogUnrealSharpEditor, Warning, "A hot reload is already in progress. Skipping hot reload request.");
		return;
	}
	
	if (bIsHotReloadPaused)
	{
		UE_LOGFMT(LogUnrealSharpEditor, Verbose, "Hot reload is currently paused. Skipping hot reload.");
		return;
	}
	
	UE_LOGFMT(LogUnrealSharpEditor, Display, "Starting C# Hot Reload...");
	
	CurrentHotReloadStatus = Active;
	double StartTime = FPlatformTime::Seconds();

	BeginCompileSession();

	FScopedSlowTask Progress(4, LOCTEXT("HotReload", "Reloading C#..."));
	Progress.MakeDialog(false, true);

	TArray<UCSManagedAssembly*> AssembliesSortedByDependencies;
	FCSAssemblyUtilities::SortAssembliesByDependencyOrder(PendingModifiedAssemblies, AssembliesSortedByDependencies);

	FString ExceptionMessage;
	if (!FCSHotReloadUtilities::RecompileDirtyProjects(AssembliesSortedByDependencies, ExceptionMessage))
	{
		CurrentHotReloadStatus = FailedToCompile;
		ReportCompileFailure(ExceptionMessage, TEXT("C# Compilation Failed"));
		return;
	}

	PendingModifiedAssemblies.Reset();

	Progress.EnterProgressFrame(1, LOCTEXT("HotReload_Reloading", "Reloading Assemblies..."));
	
	for (UCSManagedAssembly* Assembly : AssembliesSortedByDependencies)
	{
		Assembly->UnloadAssembly();
	}
	
	for (int32 i = AssembliesSortedByDependencies.Num() - 1; i >= 0; --i)
	{
		AssembliesSortedByDependencies[i]->LoadAssembly();
	}

	Progress.EnterProgressFrame(1, LOCTEXT("HotReload_Refreshing", "Refreshing Affected Blueprints..."));
	
	FCSHotReloadUtilities::RebuildDependentBlueprints(ReloadedTypes);
	
	if (bDetectedNewManagedType)
	{
		FCSHotReloadUtilities::RefreshPlacementMode();
	}
	
	if (ReloadedTypes.Num() > 0)
	{
		FCSHotReloadUtilities::RefreshBlueprintActionDatabase(ReloadedTypes);
		FCSHotReloadUtilities::RefreshStructs(ReloadedTypes);
		
		Progress.EnterProgressFrame(1, LOCTEXT("HotReload_GC", "Performing Garbage Collection..."));
		CollectGarbage(GARBAGE_COLLECTION_KEEPFLAGS);
	}
	
	CurrentHotReloadStatus = Inactive;
	bDetectedNewManagedType = false;
	ReloadedTypes.Reset();

	const double ElapsedTime = FPlatformTime::Seconds() - StartTime;
	UE_LOG(LogUnrealSharpEditor, Display, TEXT("C# Hot Reload completed in %.2f seconds."), ElapsedTime);

	FCSCompileLog& CompileLog = FCSCompileLog::Get();
	FString CompletedMessage = FString::Printf(TEXT("C# Hot Reload completed in %.2f seconds."), ElapsedTime);

	// Files rejected while parsing keep their last good version, so the reload can succeed while they are still broken.
	const int32 OutstandingErrorCount = CompileLog.GetOutstandingErrorCount();
	if (OutstandingErrorCount > 0)
	{
		CompletedMessage += FString::Printf(TEXT(" %d %s still outstanding; affected files were not reloaded."),
			OutstandingErrorCount, OutstandingErrorCount == 1 ? TEXT("error is") : TEXT("errors are"));
	}

	CompileLog.AddMessage(ECSCompileLogSeverity::Info, TEXT("HotReload"), CompletedMessage);
	EndCompileSession();

	if (OutstandingErrorCount > 0)
	{
		return;
	}

	if (TSharedPtr<SNotificationItem> Notification = CompileFailedNotification.Pin())
	{
		Notification->ExpireAndFadeout();
	}
}

bool UCSHotReloadSubsystem::HasCompileErrors() const
{
	return CurrentHotReloadStatus == FailedToCompile || FCSCompileLog::Get().HasOutstandingErrors();
}

void UCSHotReloadSubsystem::BeginCompileSession()
{
	if (bCompileSessionOpen)
	{
		return;
	}

	bCompileSessionOpen = true;
	FCSCompileLog::Get().BeginSession();
}

void UCSHotReloadSubsystem::ReportCompileFailure(const FString& ExceptionMessage, const FString& DialogTitle)
{
	FCSCompileLog& CompileLog = FCSCompileLog::Get();

	// Managed code reports structured diagnostics; fall back to the raw exception when it could not.
	if (CompileLog.GetErrorCountSinceSessionStart() == 0)
	{
		CompileLog.AddMessage(ECSCompileLogSeverity::Error, TEXT("HotReload"), DialogTitle, ExceptionMessage);
	}

	const int32 ErrorCount = FMath::Max(CompileLog.GetOutstandingErrorCount(), CompileLog.GetErrorCountSinceSessionStart());
	EndCompileSession();

	if (!FApp::CanEverRender())
	{
		return;
	}

	UnrealSharp::CompileLog::RevealTab();

	if (GetDefault<UCSUnrealSharpEditorSettings>()->bShowCompileErrorDialog)
	{
		FMessageDialog::Open(EAppMsgType::Ok, FText::FromString(ExceptionMessage), FText::FromString(DialogTitle));
		return;
	}

	ShowCompileFailedNotification(ErrorCount);
}

void UCSHotReloadSubsystem::ShowCompileFailedNotification(int32 ErrorCount)
{
	ShowCompileFailedNotification(FText::Format(LOCTEXT("CompileFailedNotification", "C# compilation failed with {0} {0}|plural(one=error,other=errors)"), ErrorCount));
}

void UCSHotReloadSubsystem::ShowCompileFailedNotification(const FText& Text)
{
	if (TSharedPtr<SNotificationItem> OldNotification = CompileFailedNotification.Pin())
	{
		OldNotification->ExpireAndFadeout();
	}

	FNotificationInfo Info(Text);
	Info.Image = UnrealSharp::Icons::GetUnrealSharpIcon_HotReloadFailed().GetIcon();
	Info.bFireAndForget = true;
	Info.ExpireDuration = 6.0f;
	Info.Hyperlink = FSimpleDelegate::CreateStatic(&UnrealSharp::CompileLog::OpenTab);
	Info.HyperlinkText = LOCTEXT("OpenCompileLog", "Open C# Compile Log");

	TSharedPtr<SNotificationItem> Notification = FSlateNotificationManager::Get().AddNotification(Info);
	if (Notification.IsValid())
	{
		Notification->SetCompletionState(SNotificationItem::CS_Fail);
	}

	CompileFailedNotification = Notification;
}

void UCSHotReloadSubsystem::OnStructRebuilt(UCSScriptStruct* NewStruct)
{
	AddReloadedType(NewStruct);
}

void UCSHotReloadSubsystem::OnClassRebuilt(UCSClass* NewClass)
{
	AddReloadedType(NewClass);
}

void UCSHotReloadSubsystem::OnEnumRebuilt(UCSEnum* NewEnum)
{
	AddReloadedType(NewEnum);
}

void UCSHotReloadSubsystem::OnInterfaceRebuilt(UCSInterface* NewInterface)
{
	AddReloadedType(NewInterface);
}

void UCSHotReloadSubsystem::AppendPendingFileChange(const TArray<FFileChangeData>& ChangedFiles, FName ProjectName)
{
	TRACE_CPUPROFILER_EVENT_SCOPE(UCSHotReloadSubsystem::AppendPendingFileChange)
	
	TArray<FFileChangeData>& PendingChangesForProject = PendingFileChanges.FindOrAdd(ProjectName);
	
	for (const FFileChangeData& ChangeData : ChangedFiles)
	{
		bool bAlreadyPending = PendingChangesForProject.ContainsByPredicate([&ChangeData](const FFileChangeData& PendingChange)
		{
			if (PendingChange.Filename == ChangeData.Filename && PendingChange.Action == ChangeData.Action)
			{
				return true;
			}
			
			return false;
		});
		
		if (!bAlreadyPending)
		{
			PendingChangesForProject.Add(ChangeData);
		}
	}
}

void UCSHotReloadSubsystem::RefreshDirectoryWatchers()
{
	TArray<FString> ProjectPaths;
	UnrealSharp::Project::GetAllProjectPaths(ProjectPaths);

	for (const FString& ProjectPath : ProjectPaths)
	{
		FString Path = FPaths::GetPath(ProjectPath);
		FName ProjectName = *FPaths::GetBaseFilename(ProjectPath);
		AddDirectoryToWatch(Path, ProjectName);
	}
}

void UCSHotReloadSubsystem::OnStopPlayingPIE(bool IsSimulating)
{
	// Replicate UE behavior, which forces a garbage collection when exiting PIE.
	UnrealSharpEditorModule->GetManagedEditorCallbacks().ForceManagedGC();
	
	if (GetDefault<UCSUnrealSharpEditorSettings>()->AutomaticHotReloading != Off)
	{
		PerformHotReload();
	}
}

bool UCSHotReloadSubsystem::Tick(float DeltaTime)
{
	if (FCSHotReloadUtilities::ShouldHotReloadOnEditorFocus(this))
	{
		PerformHotReload();
	}
	
	return true;
}

void UCSHotReloadSubsystem::AddDirectoryToWatch(const FString& Directory, FName ProjectName)
{
	if (WatchingDirectories.Contains(Directory))
	{
		return;
	}
	
	if (!FPaths::DirectoryExists(Directory))
	{
		FPlatformFileManager::Get().GetPlatformFile().CreateDirectory(*Directory);
	}

	FDirectoryWatcherModule& DirectoryWatcherModule = FModuleManager::LoadModuleChecked<FDirectoryWatcherModule>("DirectoryWatcher");
	
	FDelegateHandle Handle;
	DirectoryWatcherModule.Get()->RegisterDirectoryChangedCallback_Handle(
		Directory,
		IDirectoryWatcher::FDirectoryChanged::CreateUObject(this, &UCSHotReloadSubsystem::HandleScriptFileChanges, ProjectName),
		Handle);

	WatchingDirectories.Add(Directory);
}

void UCSHotReloadSubsystem::PauseHotReload(const FString& Reason)
{
	if (bIsHotReloadPaused)
	{
		return;
	}
	
	FString NotificationFormat = FString::Printf(TEXT("C# Reload Paused: %s"), *Reason);
	PauseNotification = FCSEditorUtilities::MakeNotification(UnrealSharp::Icons::GetUnrealSharpIcon(), NotificationFormat);
	bIsHotReloadPaused = true;
}

void UCSHotReloadSubsystem::ResumeHotReload()
{
	if (!bIsHotReloadPaused)
	{
		return;
	}

	bIsHotReloadPaused = false;
	
	if (PauseNotification.IsValid())
	{
		PauseNotification->SetText(LOCTEXT("HotReloadResumed", "C# Reload Resumed"));
		PauseNotification->SetCompletionState(SNotificationItem::CS_Success);
		PauseNotification->ExpireAndFadeout();
		PauseNotification.Reset();
	}
	
	for (const TPair<FName, TArray<FFileChangeData>>& Pair : PendingFileChanges)
	{
		HandleScriptFileChanges(Pair.Value, Pair.Key);
	}
}

void UCSHotReloadSubsystem::HandleScriptFileChanges(const TArray<FFileChangeData>& ChangedFiles, FName ProjectName)
{
	TRACE_CPUPROFILER_EVENT_SCOPE(UCSHotReloadSubsystem::HandleScriptFileChanges)
	
	if (IsHotReloading())
	{
		return;
	}
	
	TArray<FFileChangeData> CSharpFiles;
	FCSHotReloadUtilities::GetChangedCSharpFiles(ChangedFiles, CSharpFiles);
	
	if (CSharpFiles.IsEmpty())
	{
		return;
	}
	
	if (bIsHotReloadPaused)
	{
		AppendPendingFileChange(ChangedFiles, ProjectName);
		return;
	}
	
	TArray<FCSHotReloadUtilities::FCSChangedFile> DirtiedFiles;
	FCSHotReloadUtilities::CollectDirtiedFiles(CSharpFiles, DirtiedFiles);
	
	if (DirtiedFiles.IsEmpty())
	{
		return;
	}
	
	BeginCompileSession();

	FString ExceptionMessage;
	if (!FCSHotReloadUtilities::ApplyDirtiedFiles(ProjectName.ToString(), DirtiedFiles, ExceptionMessage))
	{
		CurrentHotReloadStatus = FailedToCompile;
		ReportCompileFailure(ExceptionMessage, TEXT("C# Hot Reload Error"));
		return;
	}
	
	UCSManagedAssembly* ModifiedAssembly = UCSManager::Get().FindAssembly(ProjectName);
	if (!ModifiedAssembly)
	{
		// The startup build failed for this project and no earlier build exists to load.
		const FString Message = FString::Printf(TEXT("C# project '%s' has no loaded assembly because it has never been built successfully. Fix the compile errors and restart the Editor to build it."), *ProjectName.ToString());
		UE_LOG(LogUnrealSharpEditor, Warning, TEXT("%s"), *Message);
		FCSCompileLog::Get().AddMessage(ECSCompileLogSeverity::Error, TEXT("HotReload"), Message);
		EndCompileSession();
		return;
	}

	if (!PendingModifiedAssemblies.Contains(ModifiedAssembly))
	{
		PendingModifiedAssemblies.Add(ModifiedAssembly);
	}
	
	if (FCSHotReloadUtilities::ShouldDeferHotReloadRequest(ModifiedAssembly))
	{
		UE_LOGFMT(LogUnrealSharpEditor, Verbose, "Deferring hot reload request for assembly {0}.", *ModifiedAssembly->GetName());
		return;
	}
	
	PerformHotReload();
}

#undef LOCTEXT_NAMESPACE
