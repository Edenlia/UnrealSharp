#pragma once

#include "CoreMinimal.h"
#include "Containers/Ticker.h"
#include "Framework/Commands/UIAction.h"

class FBlueprintEditor;
class UBlueprint;
class FUICommandInfo;
struct FToolMenuContext;

class FCSBlueprintEditorCustomization
{
public:
	FCSBlueprintEditorCustomization();
	~FCSBlueprintEditorCustomization();

private:
	struct FParentCommand
	{
		TSharedPtr<const FUICommandInfo> Command;
		FUIAction Original;
		FDelegateHandle InstalledExecuteHandle;
		bool bWasMapped = false;
	};

	struct FEditorState
	{
		TWeakPtr<FBlueprintEditor> Editor;
		TWeakObjectPtr<UBlueprint> Blueprint;
		TWeakObjectPtr<UClass> Parent;
		FDelegateHandle ChangedHandle;
		FDelegateHandle CompiledHandle;
		TArray<FParentCommand> ParentCommands;
		bool bOwnsOverlay = false;
		bool bPending = false;
	};

	void OnMenuGenerating(FName Name, const FToolMenuContext& Context);
	void TrackEditor(const TSharedRef<FBlueprintEditor>& Editor);
	void OnBlueprintChanged(UBlueprint* Blueprint);
	void QueueUpdate(FEditorState& State);
	bool UpdateOverlays(float DeltaTime);
	void Unbind(FEditorState& State);
	void ProtectManagedParentCommands(FEditorState& State, const TSharedRef<FBlueprintEditor>& Editor);

	TArray<FEditorState> Editors;
	FDelegateHandle MenuGeneratingHandle;
	FTSTicker::FDelegateHandle UpdateHandle;
};
