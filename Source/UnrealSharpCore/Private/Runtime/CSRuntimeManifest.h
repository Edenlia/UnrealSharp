#pragma once

#include "CoreMinimal.h"
#include "Runtime/CSRuntimeTypes.h"

/**
 * Describes which runtime a packaged build was published for. Packaging writes UnrealSharp.Runtime.json next to
 * the managed assemblies; the runtime host factory reads it to decide which FCSRuntimeHost to create.
 *
 * {
 *   "Version": 1,
 *   "Kind": "CoreClrHostFxr" | "CoreClrEmbedded" | "NativeAot",
 *   "Library": "<optional native library the host loads>",
 *   "Properties": { "<runtime property>": "<value>" }
 * }
 */
struct FCSRuntimeManifest
{
	static constexpr const TCHAR* FileName = TEXT("UnrealSharp.Runtime.json");
	/** Marker written by packaging before the manifest existed. Its presence means a NativeAOT package. */
	static constexpr const TCHAR* LegacyNativeAotMarkerName = TEXT("UnrealSharp.NativeAOT");
	static constexpr int32 CurrentVersion = 1;

	ECSRuntimeKind Kind = ECSRuntimeKind::CoreClrHostFxr;
	FString Library;
	TMap<FString, FString> Properties;
	/** File the manifest was read from, for diagnostics. */
	FString SourcePath;

	/**
	 * Reads the manifest from the managed assembly directory. Returns an unset optional when the directory holds
	 * neither a manifest nor the legacy NativeAOT marker. A manifest that exists but cannot be parsed is fatal.
	 */
	static TOptional<FCSRuntimeManifest> Load(const FString& ManagedDirectory);
};
