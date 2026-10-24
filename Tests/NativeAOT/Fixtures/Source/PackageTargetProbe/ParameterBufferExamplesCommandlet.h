#pragma once

#include "CoreMinimal.h"
#include "Commandlets/Commandlet.h"
#include "ParameterBufferExamplesCommandlet.generated.h"

UCLASS()
class UParameterBufferExamplesCommandlet : public UCommandlet
{
	GENERATED_BODY()

public:
	UParameterBufferExamplesCommandlet();
	virtual int32 Main(const FString& Params) override;
};
