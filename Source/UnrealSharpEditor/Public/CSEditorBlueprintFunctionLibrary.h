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

	/** Publish evaluated Compile documents, including linked files outside the project directory. */
	UFUNCTION(meta = (ScriptMethod))
	static void SetProjectSourceFiles(FName ProjectName, const FString& ProjectFilePath, const TArray<FString>& SourceFiles);
};
