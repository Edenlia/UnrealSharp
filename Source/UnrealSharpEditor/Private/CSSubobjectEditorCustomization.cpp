#include "CSSubobjectEditorCustomization.h"

#include "CSSourceCodeNavigation.h"
#include "Components/ActorComponent.h"
#include "Editor/UnrealEdEngine.h"
#include "Engine/SCS_Node.h"
#include "Engine/SimpleConstructionScript.h"
#include "Preferences/UnrealEdOptions.h"
#include "SourceCodeNavigation.h"
#include "SubobjectData.h"
#include "Types/CSBlueprint.h"
#include "Types/CSClass.h"
#include "UnrealEdGlobals.h"

#if UE_VERSION_OLDER_THAN(5, 7, 0)
#include "Framework/Commands/UIAction.h"
#include "SSubobjectEditor.h"
#include "SubobjectEditorMenuContext.h"
#include "ToolMenus.h"
#else
#include "Styling/AppStyle.h"
#include "Widgets/Input/SHyperlink.h"
#include "Widgets/SNullWidget.h"
#endif

#define LOCTEXT_NAMESPACE "CSSubobjectEditorCustomization"

namespace
{
	// GetBlueprint can also return the owning actor's Blueprint for components without an SCS node.
	// Match the template to verify that this Blueprint declares the component.
	const UCSBlueprint* GetManagedComponentBlueprint(const FSubobjectData& Data)
	{
		const UActorComponent* Component = Data.GetComponentTemplate();
		const UCSBlueprint* Blueprint = Component ? Cast<UCSBlueprint>(Data.GetBlueprint()) : nullptr;
		const USimpleConstructionScript* Script = Blueprint ? Blueprint->SimpleConstructionScript : nullptr;
		const USCS_Node* Node = Script ? Script->FindSCSNode(Component->GetFName()) : nullptr;
		return Node && Node->ComponentTemplate == Component ? Blueprint : nullptr;
	}

	const UCSClass* GetComponentSourceClass(const UCSBlueprint& Blueprint)
	{
		const UCSClass* SourceClass = Cast<UCSClass>(Blueprint.GeneratedClass);
		return IsValid(SourceClass) && SourceClass->HasManagedTypeDefinition() ? SourceClass : nullptr;
	}

	bool CanShowComponentSourceLinks()
	{
		return GUnrealEd && GUnrealEd->GetUnrealEdOptions()->IsCPPAllowed();
	}
}

#if UE_VERSION_OLDER_THAN(5, 7, 0)
void RegisterCSSubobjectSourceMenu(FToolMenuOwner Owner)
{
	// UE 5.6 has a component menu extension point, but no per-row widget extension.
	FToolMenuOwnerScoped OwnerScope(Owner);
	UToolMenu* Menu = UToolMenus::Get()->ExtendMenu("Kismet.SubobjectEditorContextMenu");
	FToolMenuSection& Section = Menu->FindOrAddSection("UnrealSharpComponentSource");
	Section.AddDynamicEntry("UnrealSharpComponentSourceEntry", FNewToolMenuSectionDelegate::CreateLambda([](FToolMenuSection& InSection)
	{
		const USubobjectEditorMenuContext* Context = InSection.FindContext<USubobjectEditorMenuContext>();
		const TSharedPtr<SSubobjectEditor> Editor = Context ? Context->SubobjectEditor.Pin() : nullptr;
		if (!Editor || !CanShowComponentSourceLinks())
		{
			return;
		}

		const TArray<FSubobjectDataHandle> Selection = Editor->GetSelectedHandles();
		const FSubobjectData* Data = Selection.Num() == 1 ? Selection[0].GetData() : nullptr;
		const UCSBlueprint* Blueprint = Data ? GetManagedComponentBlueprint(*Data) : nullptr;
		const UCSClass* SourceClass = Blueprint ? GetComponentSourceClass(*Blueprint) : nullptr;
		if (!SourceClass)
		{
			return;
		}

		const TWeakObjectPtr<const UClass> WeakClass(SourceClass);
		InSection.AddMenuEntry(
			"UnrealSharp.OpenComponentSource",
			LOCTEXT("OpenComponentSource", "Open C# Source"),
			LOCTEXT("OpenComponentSourceTooltip", "Open the C# class that declares this component."),
			FSourceCodeNavigation::GetOpenSourceCodeIDEIcon(),
			FUIAction(FExecuteAction::CreateLambda([WeakClass]()
			{
				FCSSourceCodeNavigation::OpenClass(WeakClass.Get());
			})));
	}));
}
#else
#if UE_VERSION_OLDER_THAN(5, 8, 0)
TSharedPtr<SWidget> FCSSubobjectEditorCustomization::GetControlsWidget(const FSubobjectData& Data) const
#else
TSharedPtr<SWidget> FCSSubobjectEditorCustomization::GetControlsWidget(TSharedRef<SSubobjectEditor>&, const FSubobjectData& Data) const
#endif
{
	const UCSBlueprint* Blueprint = GetManagedComponentBlueprint(Data);
	if (!Blueprint)
	{
		return nullptr;
	}

	const UCSClass* SourceClass = GetComponentSourceClass(*Blueprint);
	if (!SourceClass)
	{
		return SNullWidget::NullWidget;
	}

	TWeakObjectPtr<const UClass> WeakClass(SourceClass);
	return SNew(SHyperlink)
		.Style(FAppStyle::Get(), "Common.GotoNativeCodeHyperlink")
		.Text(LOCTEXT("EditInCSharp", "Edit in C#"))
		.ToolTipText(FText::Format(LOCTEXT("OpenSourceTooltip", "Open the C# declaration in {0}"), FSourceCodeNavigation::GetSelectedSourceCodeIDE()))
		.Visibility_Lambda([]()
		{
			return CanShowComponentSourceLinks() ? EVisibility::Visible : EVisibility::Collapsed;
		})
		.IsEnabled_Lambda([WeakClass]() { return WeakClass.IsValid(); })
		.OnNavigate_Lambda([WeakClass]()
		{
			FCSSourceCodeNavigation::OpenClass(WeakClass.Get());
		});
}
#endif

#undef LOCTEXT_NAMESPACE
