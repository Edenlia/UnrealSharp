#include "Types/CSScriptStruct.h"

void UCSScriptStruct::Initialize()
{
#if WITH_EDITOR
	PrimaryStruct = this;
#endif
	
	InitializeStructDefaults();
	UpdateStructFlags();
}

void UCSScriptStruct::InitializeStructDefaults()
{
	const SIZE_T Size = FMath::Max(GetStructureSize(), 1);
	const SIZE_T Alignment = GetMinAlignment();
	StructDefaults = MakeUnique<uint8[]>(Size + Alignment - 1);
	uint8* Defaults = Align(StructDefaults.Get(), Alignment);
	
	InitializeStructIgnoreDefaults(Defaults);
	
	GetManagedCallbacks().InitializeStructure(GetTypeGCHandle()->GetHandle(), Defaults);
	
	DefaultStructInstance = FUserStructOnScopeIgnoreDefaults(this, Defaults);
	DefaultStructInstance.SetPackage(GetOutermost());
}
