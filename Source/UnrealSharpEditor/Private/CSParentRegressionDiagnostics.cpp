#include "BlueprintEditor.h"
#include "EdGraphSchema_K2.h"
#include "Editor.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "Framework/Commands/InputBindingManager.h"
#include "Framework/Commands/UICommandList.h"
#include "GameFramework/Actor.h"
#include "HAL/IConsoleManager.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Misc/ScopeExit.h"
#include "SMyBlueprint.h"
#include "Subsystems/AssetEditorSubsystem.h"
#include "UnrealSharpEditor.h"

// Explicit, unsaved fixtures exercise the production My Blueprint command delegates.
// No original Blueprint or generated managed shell is modified or saved.
namespace UnrealSharp::ParentRegressionDiagnostics
{
void Run(const TArray<FString>& Args)
{
	if (!GEditor || IsRunningCommandlet() || Args.Num() != 1) return;
	UAssetEditorSubsystem* Assets = GEditor->GetEditorSubsystem<UAssetEditorSubsystem>();
	TArray<UBlueprint*> Fixtures;
	ON_SCOPE_EXIT
	{
		for (UBlueprint* Fixture : Fixtures)
		{
			Assets->CloseAllEditorsForAsset(Fixture);
			Fixture->GetPackage()->SetDirtyFlag(false);
		}
	};
	int32 Failures = 0;
	auto Verify = [&Failures](bool bCondition, const FString& Label)
	{
		Failures += !bCondition;
		UE_LOG(LogUnrealSharpEditor, Display, TEXT("PARENT_REGRESSION %s %s"), bCondition ? TEXT("PASS") : TEXT("FAIL"), *Label);
	};
	auto Create = [&Fixtures](UClass* Parent, const TCHAR* Name) -> UBlueprint*
	{
		const FString PackageName = FString(TEXT("/Game/__UnrealSharpParentRegression/")) + FGuid::NewGuid().ToString(EGuidFormats::Digits) + Name;
		UBlueprint* Blueprint = FKismetEditorUtilities::CreateBlueprint(Parent, CreatePackage(*PackageName), FName(Name), BPTYPE_Normal);
		if (Blueprint)
		{
			Fixtures.Add(Blueprint);
			FKismetEditorUtilities::CompileBlueprint(Blueprint);
		}
		return Blueprint;
	};
	UBlueprint* ManagedLeaf = LoadObject<UBlueprint>(nullptr, *Args[0]);
	UBlueprint* Parent = Create(AActor::StaticClass(), TEXT("Parent"));
	if (!Parent || !ManagedLeaf || !ManagedLeaf->ParentClass)
	{
		Verify(false, TEXT("fixture parent classes available"));
		return;
	}
	const FName Variable(TEXT("RegressionLocalVariable"));
	const FName Function(TEXT("RegressionLocalFunction"));
	const TArray<TPair<FString, UClass*>> Cases = {
		{TEXT("content-blueprint"), Parent->GeneratedClass},
		{TEXT("native"), AActor::StaticClass()},
		{TEXT("managed"), ManagedLeaf->ParentClass}
	};
	for (const auto& Case : Cases)
	{
		UBlueprint* Child = Create(Case.Value, TEXT("Child"));
		if (!Child) { Verify(false, Case.Key + TEXT(" fixture created")); continue; }
		FEdGraphPinType Type;
		Type.PinCategory = UEdGraphSchema_K2::PC_Int;
		Verify(FBlueprintEditorUtils::AddMemberVariable(Child, Variable, Type, TEXT("42")), Case.Key + TEXT(" local variable created"));
		UEdGraph* Graph = FBlueprintEditorUtils::CreateNewGraph(Child, Function, UEdGraph::StaticClass(), UEdGraphSchema_K2::StaticClass());
		FBlueprintEditorUtils::AddFunctionGraph<UClass>(Child, Graph, true, nullptr);
		FKismetEditorUtilities::CompileBlueprint(Child);
		Assets->OpenEditorForAsset(Child);
		IAssetEditorInstance* Instance = Assets->FindEditorForAsset(Child, false);
		if (!Instance || Instance->GetEditorName() != TEXT("BlueprintEditor")) { Verify(false, Case.Key + TEXT(" full editor available")); continue; }
		FBlueprintEditor* Editor = static_cast<FBlueprintEditor*>(Instance);
		TSharedPtr<SMyBlueprint> MyBlueprint = Editor->GetMyBlueprintWidget();
		if (!MyBlueprint) { Verify(false, Case.Key + TEXT(" My Blueprint available")); continue; }
		const bool bContentParent = Case.Key == TEXT("content-blueprint");
		const bool bManagedParent = Case.Key == TEXT("managed");
		// Stock Kismet recognizes managed Blueprint shells. Plugin command guards keep them read-only.
		Verify(Editor->IsParentClassABlueprint() == (bContentParent || bManagedParent), Case.Key + TEXT(" parent classification"));
		for (bool bFunction : {false, true})
		{
			MyBlueprint->Refresh();
			MyBlueprint->SelectItemByName(bFunction ? Function : Variable);
			const FName CommandName(bFunction ? TEXT("MoveFunctionToParent") : TEXT("MoveVariableToParent"));
			const TSharedPtr<FUICommandInfo> Command = FInputBindingManager::Get().FindCommandInContext(TEXT("MyBlueprint"), CommandName);
			const FUIAction* Action = Command ? Editor->GetToolkitCommands()->GetActionForCommand(Command) : nullptr;
			Verify(Action != nullptr, Case.Key + TEXT(" ") + CommandName.ToString() + TEXT(" delegate mapped"));
			if (!Action) continue;
			Verify(Action->IsVisible().IsVisible() == bContentParent, Case.Key + TEXT(" ") + CommandName.ToString() + TEXT(" visibility"));
			if (bManagedParent)
			{
				Verify(!Action->CanExecute(), CommandName.ToString() + TEXT(" managed parent execution disabled"));
				if (!Action->CanExecute())
				{
					Verify(!Action->Execute(), CommandName.ToString() + TEXT(" managed parent command rejected"));
				}
			}
			if (!bContentParent || !Action->IsVisible().IsVisible()) continue;
			Verify(Action->Execute(), CommandName.ToString() + TEXT(" production delegate executed"));
			FKismetEditorUtilities::CompileBlueprint(Parent);
			FKismetEditorUtilities::CompileBlueprint(Child);
			const bool bMoved = bFunction
				? Parent->FunctionGraphs.ContainsByPredicate([Function](UEdGraph* Item) { return Item->GetFName() == Function; }) && !Child->FunctionGraphs.Contains(Graph)
				: FBlueprintEditorUtils::FindNewVariableIndex(Parent, Variable) != INDEX_NONE && FBlueprintEditorUtils::FindNewVariableIndex(Child, Variable) == INDEX_NONE;
			Verify(bMoved, CommandName.ToString() + TEXT(" member transferred to content parent"));
			Verify(Parent->Status != BS_Error && Child->Status != BS_Error, CommandName.ToString() + TEXT(" parent and child compile"));
		}
	}
	UE_LOG(LogUnrealSharpEditor, Display, TEXT("PARENT_REGRESSION_COMPLETE failures=%d fixtures=%d saved=0"), Failures, Fixtures.Num());
}
FAutoConsoleCommand Command(TEXT("UnrealSharp.ParentRegression"),
	TEXT("<ManagedBlueprintPath>: exercise parent commands using disposable unsaved Blueprint fixtures."),
	FConsoleCommandWithArgsDelegate::CreateStatic(&Run));
}
