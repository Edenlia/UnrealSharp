#pragma once

#include "CoreMinimal.h"
#include "Engine/HitResult.h"
#include "UObject/Object.h"
#include "ParameterBufferExample.generated.h"

class AActor;

/** Reflected input, output and reference examples for inspecting generated interop code. */
UCLASS(BlueprintType)
class PACKAGETARGETFIXTURE_API UParameterBufferExample : public UObject
{
	GENERATED_BODY()

public:
	UFUNCTION(BlueprintCallable, Category = "Parameter Buffer Example")
	void Foo(int32 Count, const FString& Message, FVector Vector, const FHitResult& Hit, AActor* Actor);

	// Non-const references without UPARAM(ref) are output parameters in reflection.
	UFUNCTION(BlueprintCallable, Category = "Parameter Buffer Example")
	void MakeOutputs(int32 Seed, const FString& Prefix, AActor* Actor,
		int32& OutCount, FString& OutMessage, FVector& OutVector, FHitResult& OutHit, AActor*& OutActor);

	// UPARAM(ref) preserves the input value and exposes a C# ref parameter.
	UFUNCTION(BlueprintCallable, Category = "Parameter Buffer Example")
	void ModifyRefs(UPARAM(ref) int32& Count, UPARAM(ref) FString& Message,
		UPARAM(ref) FVector& Vector, UPARAM(ref) FHitResult& Hit,
		UPARAM(ref) AActor*& Actor, AActor* ReplacementActor);

	UFUNCTION(BlueprintCallable, Category = "Parameter Buffer Example")
	int32 Mixed(int32 Input, UPARAM(ref) int32& Value, int32& OutValue);

	UFUNCTION(BlueprintCallable, Category = "Parameter Buffer Example")
	void SwapActors(UPARAM(ref) AActor*& First, UPARAM(ref) AActor*& Second);

	UPROPERTY(BlueprintReadOnly, Category = "Parameter Buffer Example")
	int32 LastCount = 0;

	UPROPERTY(BlueprintReadOnly, Category = "Parameter Buffer Example")
	FString LastMessage;

	UPROPERTY(BlueprintReadOnly, Category = "Parameter Buffer Example")
	FVector LastVector = FVector::ZeroVector;

	UPROPERTY(BlueprintReadOnly, Category = "Parameter Buffer Example")
	FHitResult LastHit;

	// TObjectPtr is supported for members; UHT rejects it in UFUNCTION parameters.
	UPROPERTY(BlueprintReadOnly, Category = "Parameter Buffer Example")
	TObjectPtr<AActor> StoredActor;
};
