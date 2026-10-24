#include "ParameterBufferTestRunner.h"

#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"
#include "UObject/StructOnScope.h"
#include "UObject/UObjectIterator.h"
#include "UObject/UnrealType.h"

int32 RunParameterBufferTests(const FString& Params)
{
	FString OutputPath = FPaths::ProjectSavedDir() / TEXT("ParameterBufferExamples/results.txt");
	FParse::Value(*Params, TEXT("TestReport="), OutputPath);
	FString Report;
	auto Finish = [&](int32 ExitCode)
	{
		IFileManager::Get().MakeDirectory(*FPaths::GetPath(OutputPath), true);
		if (!FFileHelper::SaveStringToFile(Report, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
		{
			return 3;
		}
		UE_LOG(LogTemp, Display, TEXT("ParameterBufferExamples exit=%d report=%s\n%s"), ExitCode, *OutputPath, *Report);
		return ExitCode;
	};

	UClass* ExampleClass = LoadObject<UClass>(nullptr, TEXT("/Script/PackageTargetFixture.ParameterBufferExample"));
	if (!ExampleClass)
	{
		Report += TEXT("FAIL native example class missing\n");
		return Finish(1);
	}

	// Record actual runtime sizes, offsets and parameter flags alongside test results.
	for (const TCHAR* Name : {TEXT("Foo"), TEXT("MakeOutputs"), TEXT("ModifyRefs"), TEXT("Mixed"), TEXT("SwapActors")})
	{
		UFunction* Function = ExampleClass->FindFunctionByName(Name);
		if (!Function)
		{
			Report += FString::Printf(TEXT("FAIL function missing: %s\n"), Name);
			return Finish(1);
		}
		Report += FString::Printf(TEXT("LAYOUT %s ParmsSize=%u ReturnValueOffset=%u\n"), Name, Function->ParmsSize, Function->ReturnValueOffset);
		for (TFieldIterator<FProperty> It(Function); It; ++It)
		{
			FProperty* Property = *It;
			if (!Property->HasAnyPropertyFlags(CPF_Parm)) continue;
			Report += FString::Printf(TEXT("  %s type=%s offset=%d size=%d Out=%d Ref=%d Const=%d Return=%d POD=%d NoDestructor=%d TObjectPtr=%d\n"),
				*Property->GetName(), *Property->GetCPPType(), Property->GetOffset_ForUFunction(), Property->GetSize(),
				Property->HasAnyPropertyFlags(CPF_OutParm), Property->HasAnyPropertyFlags(CPF_ReferenceParm),
				Property->HasAnyPropertyFlags(CPF_ConstParm), Property->HasAnyPropertyFlags(CPF_ReturnParm),
				Property->HasAnyPropertyFlags(CPF_IsPlainOldData), Property->HasAnyPropertyFlags(CPF_NoDestructor),
				Property->HasAnyPropertyFlags(CPF_TObjectPtr));
		}
	}

	UClass* TestsClass = nullptr;
	for (TObjectIterator<UClass> It; It; ++It)
	{
		if (It->GetName().Contains(TEXT("ParameterBufferManagedTests"))
			&& !It->GetName().StartsWith(TEXT("SKEL_"))
			&& !It->HasAnyClassFlags(CLASS_Abstract | CLASS_Deprecated | CLASS_NewerVersionExists)
			&& It->FindFunctionByName(TEXT("RunTests")))
		{
			TestsClass = *It;
			break;
		}
	}
	UFunction* RunTests = TestsClass ? TestsClass->FindFunctionByName(TEXT("RunTests")) : nullptr;
	FObjectProperty* First = RunTests ? FindFProperty<FObjectProperty>(RunTests, TEXT("First")) : nullptr;
	FObjectProperty* Second = RunTests ? FindFProperty<FObjectProperty>(RunTests, TEXT("Second")) : nullptr;
	FBoolProperty* Result = RunTests ? CastField<FBoolProperty>(RunTests->GetReturnProperty()) : nullptr;
	FStrProperty* ManagedReport = TestsClass ? FindFProperty<FStrProperty>(TestsClass, TEXT("Report")) : nullptr;
	if (!First || !Second || !Result || !ManagedReport)
	{
		Report += TEXT("FAIL managed test class/function/properties missing; build and load PackagingManagedProbe first\n");
		return Finish(2);
	}

	UWorld* World = UWorld::CreateWorld(EWorldType::Game, false);
	AActor* FirstActor = World ? World->SpawnActor<AActor>() : nullptr;
	AActor* SecondActor = World ? World->SpawnActor<AActor>() : nullptr;
	if (!FirstActor || !SecondActor)
	{
		if (World) World->DestroyWorld(false);
		Report += TEXT("FAIL temporary Actor creation\n");
		return Finish(2);
	}

	UObject* Tests = NewObject<UObject>(GetTransientPackage(), TestsClass);
	FStructOnScope Arguments(RunTests);
	First->SetObjectPropertyValue_InContainer(Arguments.GetStructMemory(), FirstActor);
	Second->SetObjectPropertyValue_InContainer(Arguments.GetStructMemory(), SecondActor);
	Tests->ProcessEvent(RunTests, Arguments.GetStructMemory());
	const bool Passed = Result->GetPropertyValue_InContainer(Arguments.GetStructMemory());
	Report += TEXT("\nMANAGED CLASS ") + TestsClass->GetPathName() + TEXT("\n");
	Report += ManagedReport->GetPropertyValue_InContainer(Tests);
	World->DestroyWorld(false);
	return Finish(Passed ? 0 : 1);
}
