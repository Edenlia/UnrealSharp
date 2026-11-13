#include "Runtime/CSRuntimeManifest.h"

#include "UnrealSharpCore.h"
#include "Dom/JsonObject.h"
#include "Logging/StructuredLog.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

namespace
{
	bool ParseRuntimeKind(const FString& Text, ECSRuntimeKind& OutKind)
	{
		for (ECSRuntimeKind Kind : { ECSRuntimeKind::CoreClrHostFxr, ECSRuntimeKind::CoreClrEmbedded, ECSRuntimeKind::NativeAot })
		{
			if (Text.Equals(LexToString(Kind), ESearchCase::IgnoreCase))
			{
				OutKind = Kind;
				return true;
			}
		}
		return false;
	}

	/** Reads an optional object of string values. Returns false (after logging) if it has a non-string value. */
	bool ReadStringMap(const FJsonObject& Root, const TCHAR* FieldName, const FString& ManifestPath, TMap<FString, FString>& OutMap)
	{
		const TSharedPtr<FJsonObject>* Object = nullptr;
		if (!Root.TryGetObjectField(FieldName, Object))
		{
			return true;
		}

		for (const TPair<FString, TSharedPtr<FJsonValue>>& Entry : (*Object)->Values)
		{
			FString Value;
			if (!Entry.Value.IsValid() || !Entry.Value->TryGetString(Value))
			{
				UE_LOGFMT(LogUnrealSharp, Fatal, "'{0}.{1}' in {2} must be a string", FieldName, Entry.Key, ManifestPath);
				return false;
			}
			OutMap.Add(Entry.Key, MoveTemp(Value));
		}
		return true;
	}
}

TOptional<FCSRuntimeManifest> FCSRuntimeManifest::Load(const FString& ManagedDirectory)
{
	const FString ManifestPath = ManagedDirectory / FileName;
	if (!FPaths::FileExists(ManifestPath))
	{
		const FString MarkerPath = ManagedDirectory / LegacyNativeAotMarkerName;
		if (!FPaths::FileExists(MarkerPath))
		{
			return {};
		}

		FCSRuntimeManifest Manifest;
		Manifest.Kind = ECSRuntimeKind::NativeAot;
		Manifest.SourcePath = MarkerPath;
		return Manifest;
	}

	FString Json;
	if (!FFileHelper::LoadFileToString(Json, *ManifestPath))
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "Failed to read the UnrealSharp runtime manifest: {0}", ManifestPath);
		return {};
	}

	TSharedPtr<FJsonObject> Root;
	if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json), Root) || !Root.IsValid())
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "The UnrealSharp runtime manifest is not valid JSON: {0}", ManifestPath);
		return {};
	}

	int32 Version = 0;
	if (!Root->TryGetNumberField(TEXT("Version"), Version) || Version != CurrentVersion)
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "Unsupported UnrealSharp runtime manifest version {0} (expected {1}): {2}",
			Version, CurrentVersion, ManifestPath);
		return {};
	}

	FCSRuntimeManifest Manifest;
	Manifest.SourcePath = ManifestPath;

	FString KindText;
	if (!Root->TryGetStringField(TEXT("Kind"), KindText) || !ParseRuntimeKind(KindText, Manifest.Kind))
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "Unknown runtime kind '{0}' in {1}", KindText, ManifestPath);
		return {};
	}

	Root->TryGetStringField(TEXT("Library"), Manifest.Library);

	if (!ReadStringMap(*Root, TEXT("Properties"), ManifestPath, Manifest.Properties)
		|| !ReadStringMap(*Root, TEXT("NativeLibraries"), ManifestPath, Manifest.NativeLibraries)
		|| !ReadStringMap(*Root, TEXT("ReadyToRunImages"), ManifestPath, Manifest.ReadyToRunImages))
	{
		return {};
	}

	return Manifest;
}
