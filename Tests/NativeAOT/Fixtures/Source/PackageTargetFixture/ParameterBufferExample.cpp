#include "ParameterBufferExample.h"
#include "GameFramework/Actor.h"

void UParameterBufferExample::Foo(int32 Count, const FString& Message, FVector Vector, const FHitResult& Hit, AActor* Actor)
{
	LastCount = Count;
	LastMessage = Message;
	LastVector = Vector;
	LastHit = Hit;
	StoredActor = Actor;
	UE_LOG(LogTemp, Display, TEXT("ParameterBufferExample: Count=%d, Vector=%s, Hit.Distance=%g, Actor=%s"),
		Count, *Vector.ToString(), Hit.Distance, *GetNameSafe(Actor));
}

void UParameterBufferExample::MakeOutputs(int32 Seed, const FString& Prefix, AActor* Actor,
	int32& OutCount, FString& OutMessage, FVector& OutVector, FHitResult& OutHit, AActor*& OutActor)
{
	OutCount = Seed + 10;
	OutMessage = Prefix + TEXT("|out");
	OutVector = FVector(Seed, Seed + 1, Seed + 2);
	OutHit = FHitResult();
	OutHit.Location = OutVector;
	OutHit.ImpactPoint = FVector(4, 5, 6);
	OutHit.Distance = 12.5f;
	OutHit.Item = 4;
	OutHit.bBlockingHit = true;
	OutActor = Actor;
}

void UParameterBufferExample::ModifyRefs(int32& Count, FString& Message, FVector& Vector,
	FHitResult& Hit, AActor*& Actor, AActor* ReplacementActor)
{
	Count += 10;
	Message += TEXT("|native");
	Vector += FVector(10, 20, 30);
	Hit.Location += FVector(1, 2, 3);
	Hit.Distance += 5;
	Hit.Item += 1;
	Hit.bBlockingHit = !Hit.bBlockingHit;
	Actor = ReplacementActor;
}

int32 UParameterBufferExample::Mixed(int32 Input, int32& Value, int32& OutValue)
{
	Value += Input;
	OutValue = Value * 2;
	return Value + OutValue;
}

void UParameterBufferExample::SwapActors(AActor*& First, AActor*& Second)
{
	Swap(First, Second);
}
