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
	bool IsNativeAOT() const;
private:
	TUniquePtr<FCSRuntimeHost> RuntimeHost;
};
