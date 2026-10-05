#include "Factories/PropertyGenerators/CSEnumPropertyGenerator.h"
#include "CSManager.h"
#include "Types/CSEnum.h"
#include "ReflectionData/CSFieldType.h"
#include "Engine/UserDefinedEnum.h"
#include "Json/CSRapidJsonUtilties.h"

namespace
{
struct FCSMappedEnumType : FCSFieldType
{
	FString BlueprintEnumPath;
	TArray<int64> BlueprintEnumValues;

	virtual bool Serialize(FConstObject JsonObject) override
	{
		if (!FCSFieldType::Serialize(JsonObject)) return false;
		if (!UnrealSharp::RapidJson::FindMember(JsonObject, TEXT("BlueprintEnumPath")).IsSet()) return true;
		const auto Path = UnrealSharp::RapidJson::GetStringField(JsonObject, TEXT("BlueprintEnumPath"));
		const auto Values = UnrealSharp::RapidJson::GetArrayField(JsonObject, TEXT("BlueprintEnumValues"));
		if (!Path.IsSet() || Path->IsEmpty() || !Values.IsSet() || Values->Empty()) return false;
		BlueprintEnumPath = FString(*Path);
		BlueprintEnumValues.Reset();
		for (const UnrealSharp::RapidJson::FValue& JsonValue : *Values)
		{
			if (!JsonValue.IsInt64()) return false;
			const int64 Value = JsonValue.GetInt64();
			if (Value < 0 || Value > 255 || BlueprintEnumValues.Contains(Value)) return false;
			BlueprintEnumValues.Add(Value);
		}
		return true;
	}
};
}

UCSEnumPropertyGenerator::UCSEnumPropertyGenerator()
{
	EnumRedirectors.Add("EObjectChannel", StaticEnum<EObjectTypeQuery>());
	EnumRedirectors.Add("ETraceChannel", StaticEnum<ETraceTypeQuery>());
}

FProperty* UCSEnumPropertyGenerator::CreateProperty(UField* Outer, const FCSPropertyReflectionData& PropertyReflectionData)
{
	const TSharedPtr<FCSMappedEnumType> EnumType = PropertyReflectionData.GetInnerTypeData<FCSMappedEnumType>();
	const FCSFieldName& FieldName = EnumType->InnerType;
	
	UEnum* Enum = nullptr;
	if (!EnumType->BlueprintEnumPath.IsEmpty())
	{
		Enum = LoadObject<UUserDefinedEnum>(nullptr, *EnumType->BlueprintEnumPath);
		bool bMatches = IsValid(Enum) && Enum->GetPathName() == EnumType->BlueprintEnumPath
			&& Enum->NumEnums() == EnumType->BlueprintEnumValues.Num() + 1;
		if (bMatches)
		{
			for (int32 Index = 0; Index < EnumType->BlueprintEnumValues.Num(); ++Index)
			{
				bMatches &= Enum->GetValueByIndex(Index) == EnumType->BlueprintEnumValues[Index];
			}
		}
		if (!bMatches)
		{
			UE_LOGFMT(LogUnrealSharp, Fatal, "Blueprint enum mapping does not match asset {0}. PropertyName: {1}",
				EnumType->BlueprintEnumPath, PropertyReflectionData.GetName());
		}
	}
	else if (UEnum** FoundRedirector = EnumRedirectors.Find(FieldName.GetEngineFName()))
	{
		Enum = *FoundRedirector;
	}
	else
	{
		UCSManagedAssembly* Assembly = UCSManager::Get().FindAssembly(EnumType->InnerType.GetAssemblyName());
		Enum = IsValid(Assembly) ? Assembly->ResolveUField<UEnum>(FieldName) : nullptr;
	}
	
	if (!IsValid(Enum))
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "Enum is not valid in {0}. PropertyName: {1}", __FUNCTION__, *PropertyReflectionData.GetName().ToString());
	}

#if WITH_EDITOR
	if (UCSEnum* ManagedEnum = Cast<UCSEnum>(Enum))
	{
		if (UStruct* OwningClass = TryFindingOwningClass(Outer))
		{
			ManagedEnum->GetManagedReferencesCollection().AddReference(OwningClass);
		}
	}
#endif
	
	FProperty* NewEnumProperty;
	if (Enum->GetCppForm() == UEnum::ECppForm::EnumClass)
	{
		FEnumProperty* EnumProperty = NewProperty<FEnumProperty>(Outer, PropertyReflectionData, FEnumProperty::StaticClass());
		
#if ENGINE_MINOR_VERSION >= 8
		FByteProperty* UnderlyingProp = new FByteProperty(EnumProperty, "UnderlyingType");
#else
		FByteProperty* UnderlyingProp = new FByteProperty(EnumProperty, "UnderlyingType", RF_Public);
#endif
		
		EnumProperty->SetEnum(Enum);
		EnumProperty->AddCppProperty(UnderlyingProp);
		NewEnumProperty = EnumProperty;
	}
	else
	{
		FByteProperty* ByteProperty = NewProperty<FByteProperty>(Outer, PropertyReflectionData, FByteProperty::StaticClass());
		ByteProperty->Enum = Enum;
		NewEnumProperty = ByteProperty;
	}
	
	return NewEnumProperty;
}

TSharedPtr<FCSUnrealType> UCSEnumPropertyGenerator::CreatePropertyInnerTypeData(ECSPropertyType PropertyType)
{
	return MakeShared<FCSMappedEnumType>();
}
