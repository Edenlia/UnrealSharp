#pragma once

#include "Misc/EngineVersionComparison.h"

#if UE_VERSION_OLDER_THAN(5, 7, 0)
struct FToolMenuOwner;
void RegisterCSSubobjectSourceMenu(FToolMenuOwner Owner);
#else
#include "ISCSEditorUICustomization.h"

class FCSSubobjectEditorCustomization : public ISCSEditorUICustomization
{
public:
#if UE_VERSION_OLDER_THAN(5, 8, 0)
	virtual TSharedPtr<SWidget> GetControlsWidget(const FSubobjectData& Data) const override;
#else
	virtual TSharedPtr<SWidget> GetControlsWidget(TSharedRef<SSubobjectEditor>& SubobjectEditor, const FSubobjectData& Data) const override;
#endif
};
#endif
