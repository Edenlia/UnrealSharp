#include "BlueprintEditor.h"
#include "Dom/JsonObject.h"
#include "Editor.h"
#include "Features/IModularFeatures.h"
#include "Framework/Application/SlateApplication.h"
#include "HAL/IConsoleManager.h"
#include "ISourceCodeAccessModule.h"
#include "ISourceCodeAccessor.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"
#include "Subsystems/AssetEditorSubsystem.h"
#include "UnrealSharpEditor.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/SWindow.h"

// Opt-in integration diagnostics. Invoke via CmdLink without desktop input or changing accessor preferences.
namespace UnrealSharp::SourceNavigationDiagnostics
{
class FCaptureAccessor : public ISourceCodeAccessor
{
public:
	FCaptureAccessor(ISourceCodeAccessor& InOriginal, bool bInForward)
		: Original(InOriginal), bForward(bInForward) {}

	virtual void RefreshAvailability() override { Original.RefreshAvailability(); }
	virtual bool CanAccessSourceCode() const override { return Original.CanAccessSourceCode(); }
	virtual FName GetFName() const override { return TEXT("UnrealSharpNavigationCapture"); }
	virtual FText GetNameText() const override { return Original.GetNameText(); }
	virtual FText GetDescriptionText() const override { return Original.GetDescriptionText(); }
	virtual bool OpenSolution() override { return Original.OpenSolution(); }
	virtual bool OpenSolutionAtPath(const FString& Path) override { return Original.OpenSolutionAtPath(Path); }
	virtual bool DoesSolutionExist() const override { return Original.DoesSolutionExist(); }
	virtual bool OpenSourceFiles(const TArray<FString>& Paths) override { return Original.OpenSourceFiles(Paths); }
	virtual bool AddSourceFiles(const TArray<FString>& Paths, const TArray<FString>& Modules) override { return Original.AddSourceFiles(Paths, Modules); }
	virtual bool SaveAllOpenDocuments() const override { return Original.SaveAllOpenDocuments(); }
	virtual void Tick(float DeltaTime) override {}
	virtual bool OpenFileAtLine(const FString& Path, int32 Line, int32 Column) override
	{
		TSharedRef<FJsonObject> Request = MakeShared<FJsonObject>();
		Request->SetStringField(TEXT("file"), Path);
		Request->SetNumberField(TEXT("line"), Line);
		Request->SetNumberField(TEXT("column"), Column);
		const bool bAccepted = !bForward || Original.OpenFileAtLine(Path, Line, Column);
		Request->SetBoolField(TEXT("accepted"), bAccepted);
		Requests.Add(MakeShared<FJsonValueObject>(Request));
		return bAccepted;
	}

	TArray<TSharedPtr<FJsonValue>> Requests;
private:
	ISourceCodeAccessor& Original;
	bool bForward;
};

struct FWidgetRecord
{
	TSharedRef<SWidget> Widget;
	bool bParentVisible;
};

void CollectWidgets(const TSharedRef<SWidget>& Widget, bool bParentVisible, TArray<FWidgetRecord>& Records)
{
	const FString Tag = Widget->GetTag().ToString();
	if (Tag.StartsWith(TEXT("BlueprintEditor.ParentClass")) || Tag == TEXT("BlueprintEditor.FindParentClass")
		|| Tag == TEXT("BlueprintEditor.EditParentClass") || Tag == TEXT("UnrealSharp.EditInCSharp"))
	{
		Records.Add({Widget, bParentVisible});
	}
	const bool bVisible = bParentVisible && Widget->GetVisibility().IsVisible();
	FChildren* Children = Widget->GetChildren();
	for (int32 Index = 0; Children && Index < Children->Num(); ++Index)
	{
		CollectWidgets(Children->GetChildAt(Index), bVisible, Records);
	}
}

void Run(const TArray<FString>& Args)
{
	if (Args.Num() < 2 || !GEditor || IsRunningCommandlet())
	{
		UE_LOG(LogUnrealSharpEditor, Warning, TEXT("Usage: UnrealSharp.SourceNavigation <BlueprintPath> <open|inspect|parent|edit|component|browse> [launch]"));
		return;
	}
	UBlueprint* Blueprint = LoadObject<UBlueprint>(nullptr, *Args[0]);
	if (!Blueprint)
	{
		UE_LOG(LogUnrealSharpEditor, Error, TEXT("SourceNavigation: Blueprint not found: %s"), *Args[0]);
		return;
	}
	UAssetEditorSubsystem* Assets = GEditor->GetEditorSubsystem<UAssetEditorSubsystem>();
	if (Args[1] == TEXT("open"))
	{
		Assets->OpenEditorForAsset(Blueprint);
		return; // Inspect on a subsequent tick after Slate has arranged the widgets.
	}
	IAssetEditorInstance* Instance = Assets->FindEditorForAsset(Blueprint, false);
	if (!Instance || Instance->GetEditorName() != TEXT("BlueprintEditor"))
	{
		UE_LOG(LogUnrealSharpEditor, Error, TEXT("SourceNavigation: open the Blueprint editor first."));
		return;
	}
	TArray<FWidgetRecord> Widgets;
	for (const TSharedRef<SWindow>& Window : FSlateApplication::Get().GetTopLevelWindows())
	{
		CollectWidgets(Window, true, Widgets);
	}
	ISourceCodeAccessModule& Access = FModuleManager::LoadModuleChecked<ISourceCodeAccessModule>(TEXT("SourceCodeAccess"));
	ISourceCodeAccessor& Original = Access.GetAccessor();
	const FName OriginalName = Original.GetFName();
	const bool bLaunch = Args.Num() > 2 && Args[2] == TEXT("launch");
	FCaptureAccessor Capture(Original, bLaunch);
	IModularFeatures::Get().RegisterModularFeature(TEXT("SourceCodeAccessor"), &Capture);
	Access.SetAccessor(Capture.GetFName());
	// Source opening is deferred to a later tick by default; open synchronously so the capture sees it.
	IConsoleVariable* AsyncNavigation = IConsoleManager::Get().FindConsoleVariable(TEXT("SourceCodeNavigation.EnableAsync"));
	const bool bAsyncNavigation = AsyncNavigation && AsyncNavigation->GetBool();
	if (AsyncNavigation)
	{
		AsyncNavigation->Set(false, ECVF_SetByCode);
	}
	ON_SCOPE_EXIT
	{
		if (AsyncNavigation)
		{
			AsyncNavigation->Set(bAsyncNavigation, ECVF_SetByCode);
		}
		Access.SetAccessor(OriginalName);
		IModularFeatures::Get().UnregisterModularFeature(TEXT("SourceCodeAccessor"), &Capture);
	};

	TSharedRef<FJsonObject> Report = MakeShared<FJsonObject>();
	Report->SetStringField(TEXT("blueprint"), Blueprint->GetPathName());
	Report->SetStringField(TEXT("parent"), Blueprint->ParentClass ? Blueprint->ParentClass->GetPathName() : TEXT(""));
	Report->SetStringField(TEXT("accessor"), OriginalName.ToString());
	Report->SetBoolField(TEXT("forwarded_to_ide"), bLaunch);
	TArray<TSharedPtr<FJsonValue>> WidgetReports;
	TArray<TSharedRef<SButton>> Targets;
	const FString ActionTag = Args[1] == TEXT("parent") ? TEXT("BlueprintEditor.ParentClassSource")
		: Args[1] == TEXT("edit") ? TEXT("BlueprintEditor.EditParentClass")
		: Args[1] == TEXT("component") ? TEXT("UnrealSharp.EditInCSharp")
		: Args[1] == TEXT("browse") ? TEXT("BlueprintEditor.FindParentClass") : TEXT("");
	for (const FWidgetRecord& Record : Widgets)
	{
		if (!Record.bParentVisible) continue;
		const TSharedRef<SWidget>& Widget = Record.Widget;
		TSharedRef<FJsonObject> Item = MakeShared<FJsonObject>();
		Item->SetStringField(TEXT("tag"), Widget->GetTag().ToString());
		Item->SetBoolField(TEXT("visible"), Widget->GetVisibility().IsVisible());
		Item->SetBoolField(TEXT("enabled"), Widget->IsEnabled());
		Item->SetNumberField(TEXT("x"), Widget->GetCachedGeometry().GetAbsolutePosition().X);
		WidgetReports.Add(MakeShared<FJsonValueObject>(Item));
		if (Widget->GetTag() == TEXT("BlueprintEditor.FindParentClass"))
		{
			Report->SetBoolField(TEXT("browse_visible"), Widget->GetVisibility().IsVisible());
		}
		if (Widget->GetTag() == TEXT("BlueprintEditor.EditParentClass"))
		{
			Report->SetBoolField(TEXT("edit_visible"), Widget->GetVisibility().IsVisible());
		}
		if (!ActionTag.IsEmpty() && Widget->GetTag() == FName(*ActionTag)
			&& ((Widget->GetVisibility().IsVisible() && Widget->IsEnabled()) || Args[1] == TEXT("browse")))
		{
			Targets.Add(StaticCastSharedRef<SButton>(Widget));
		}
	}
	Report->SetArrayField(TEXT("widgets"), WidgetReports);
	const int32 OpenAssetsBefore = Assets->GetAllEditedAssets().Num();
	if (Args[1] == TEXT("asset"))
	{
		if (Blueprint->ParentClass && Blueprint->ParentClass->ClassGeneratedBy)
		{
			Assets->OpenEditorForAsset(Blueprint->ParentClass->ClassGeneratedBy);
		}
	}
	else if (Targets.Num() == 1 || Args[1] == TEXT("component"))
	{
		for (const TSharedRef<SButton>& Target : Targets)
		{
			Target->SimulateClick();
		}
	}
	Report->SetNumberField(TEXT("matching_buttons"), Targets.Num());
	Report->SetNumberField(TEXT("open_assets_before"), OpenAssetsBefore);
	Report->SetNumberField(TEXT("open_assets_after"), Assets->GetAllEditedAssets().Num());
	Report->SetArrayField(TEXT("source_requests"), Capture.Requests);
	FString Json;
	FJsonSerializer::Serialize(Report, TJsonWriterFactory<>::Create(&Json));
	UE_LOG(LogUnrealSharpEditor, Display, TEXT("SOURCE_NAVIGATION_REPORT %s"), *Json);
}

FAutoConsoleCommand Command(TEXT("UnrealSharp.SourceNavigation"),
	TEXT("Inspect Blueprint source-navigation controls or invoke their Slate click delegates. IDE launch is opt-in."),
	FConsoleCommandWithArgsDelegate::CreateStatic(&Run));
}
