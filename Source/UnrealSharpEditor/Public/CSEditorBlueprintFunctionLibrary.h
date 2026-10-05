#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "CSEditorBlueprintFunctionLibrary.generated.h"

UCLASS()
class UCSEditorBlueprintFunctionLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()
public:
	UFUNCTION(meta = (ScriptMethod))
	static void AddAssemblyDependencies(FName AssemblyName, const TArray<FName>& DependentAssemblyNames);

	/** Register evaluated Compile documents, including linked sources outside the project directory. */
	UFUNCTION(meta = (ScriptMethod))
	static void SetProjectSourceFiles(FName ProjectName, const TArray<FString>& SourceFiles);
};
