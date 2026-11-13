#pragma once

#include "CoreMinimal.h"
#include "Modules/ModuleManager.h"
#include "Runtime/CSRuntimeTypes.h"
#include "Templates/UniquePtr.h"

class FCSRuntimeHost;

DECLARE_LOG_CATEGORY_EXTERN(LogUnrealSharp, Log, All);

class FUnrealSharpCoreModule : public IModuleInterface
{
public:
	FUnrealSharpCoreModule();
	virtual ~FUnrealSharpCoreModule() override;

	virtual void StartupModule() override;
	virtual void ShutdownModule() override;

	/** Kind of the runtime this process started. Only meaningful after the module has started up. */
	ECSRuntimeKind GetRuntimeKind() const;
	/** What the active runtime supports. Prefer this over checking GetRuntimeKind(). */
	FCSRuntimeCapabilities GetRuntimeCapabilities() const;

	static FUnrealSharpCoreModule& Get()
	{
		return FModuleManager::GetModuleChecked<FUnrealSharpCoreModule>(TEXT("UnrealSharpCore"));
	}

	UE_DEPRECATED(5.6, "Test GetRuntimeCapabilities() for the behavior you need instead.")
	bool IsNativeAOT() const { return GetRuntimeCapabilities().bAssembliesArePrelinked; }
private:
	TUniquePtr<FCSRuntimeHost> RuntimeHost;
};
