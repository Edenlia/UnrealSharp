#include "CompileLog/CSCompileLogTab.h"

#include "ISourceCodeAccessModule.h"
#include "ISourceCodeAccessor.h"
#include "LevelEditor.h"
#include "SCSCompileLog.h"
#include "WorkspaceMenuStructure.h"
#include "WorkspaceMenuStructureModule.h"
#include "Framework/Docking/LayoutExtender.h"
#include "Framework/Docking/TabManager.h"
#include "Styling/AppStyle.h"
#include "Widgets/Docking/SDockTab.h"

#define LOCTEXT_NAMESPACE "CSCompileLogTab"

namespace UnrealSharp::CompileLog
{
	const FName TabId(TEXT("UnrealSharpCompileLog"));

	static FDelegateHandle LayoutExtensionHandle;

	static TSharedRef<SDockTab> SpawnTab(const FSpawnTabArgs& Args)
	{
		return SNew(SDockTab)
			.TabRole(ETabRole::NomadTab)
			.Label(LOCTEXT("TabLabel", "C# Compile Log"))
			[
				SNew(SCSCompileLog)
			];
	}

	void RegisterTab()
	{
		FGlobalTabmanager::Get()->RegisterNomadTabSpawner(TabId, FOnSpawnTab::CreateStatic(&SpawnTab))
			.SetDisplayName(LOCTEXT("TabTitle", "C# Compile Log"))
			.SetTooltipText(LOCTEXT("TabTooltip", "Open the UnrealSharp C# Compile Log, which lists C# compiler errors and warnings."))
			.SetGroup(WorkspaceMenu::GetMenuStructure().GetDeveloperToolsLogCategory())
			.SetIcon(FSlateIcon(FAppStyle::GetAppStyleSetName(), "Log.TabIcon"));

		// Dock next to the Output Log in the Level Editor layout.
		FLevelEditorModule& LevelEditorModule = FModuleManager::LoadModuleChecked<FLevelEditorModule>("LevelEditor");
		LayoutExtensionHandle = LevelEditorModule.OnRegisterLayoutExtensions().AddLambda([](FLayoutExtender& Extender)
		{
			Extender.ExtendLayout(FTabId(TEXT("OutputLog")), ELayoutExtensionPosition::After, FTabManager::FTab(TabId, ETabState::ClosedTab));
		});
	}

	void UnregisterTab()
	{
		if (FLevelEditorModule* LevelEditorModule = FModuleManager::GetModulePtr<FLevelEditorModule>("LevelEditor"))
		{
			LevelEditorModule->OnRegisterLayoutExtensions().Remove(LayoutExtensionHandle);
		}

		if (FSlateApplication::IsInitialized())
		{
			FGlobalTabmanager::Get()->UnregisterNomadTabSpawner(TabId);
		}
	}

	void OpenTab()
	{
		FGlobalTabmanager::Get()->TryInvokeTab(TabId);
	}

	void RevealTab()
	{
		const TSharedPtr<SDockTab> ExistingTab = FGlobalTabmanager::Get()->FindExistingLiveTab(FTabId(TabId));
		if (ExistingTab.IsValid() && ExistingTab->IsForeground())
		{
			return;
		}

		// An existing tab is brought to front and flashed by the tab manager; a newly spawned tab is not flashed.
		const TSharedPtr<SDockTab> Tab = FGlobalTabmanager::Get()->TryInvokeTab(TabId);
		if (Tab.IsValid() && !ExistingTab.IsValid())
		{
			Tab->FlashTab();
		}
	}

	void OpenSourceAt(const FString& File, int32 Line, int32 Column)
	{
		if (!FPaths::FileExists(File))
		{
			return;
		}

		ISourceCodeAccessModule& SourceCodeAccessModule = FModuleManager::LoadModuleChecked<ISourceCodeAccessModule>("SourceCodeAccess");
		if (SourceCodeAccessModule.GetAccessor().OpenFileAtLine(File, Line, Column))
		{
			return;
		}

		FPlatformProcess::LaunchFileInDefaultExternalApplication(*File);
	}
}

#undef LOCTEXT_NAMESPACE
