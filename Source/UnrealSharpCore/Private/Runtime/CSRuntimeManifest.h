#pragma once

#include "CoreMinimal.h"
#include "Runtime/CSRuntimeTypes.h"

/** How a CoreClrEmbedded package hands its managed assemblies to the runtime. */
enum class ECSAssemblySource : uint8
{
	/** Assembly files on disk, listed as trusted platform assemblies. */
	Files,
	/** Assemblies read through the Unreal file system and returned from external_assembly_probe. */
	Probe,
};

/**
 * Describes which runtime a packaged build was published for. Packaging writes UnrealSharp.Runtime.json next to
 * the managed assemblies; the runtime host factory reads it to decide which FCSRuntimeHost to create.
 *
 * {
 *   "Version": 1,
 *   "Kind": "CoreClrHostFxr" | "CoreClrEmbedded" | "NativeAot",
 *   "Library": "<optional native library the host loads>",
 *   "AssemblySource": "Files" | "Probe",
 *   "Properties": { "<runtime property>": "<value>" },
 *   "NativeLibraries": { "<p/invoke library name>": "<library reference>" },
 *   "ReadyToRunImages": { "<composite image name>": "<library reference>" }
 * }
 *
 * Library references use the platform convention of PlatformUtilities::ResolveBundledLibraryPath.
 */
struct FCSRuntimeManifest
{
	static constexpr const TCHAR* FileName = TEXT("UnrealSharp.Runtime.json");
	/** Marker written by packaging before the manifest existed. Its presence means a NativeAOT package. */
	static constexpr const TCHAR* LegacyNativeAotMarkerName = TEXT("UnrealSharp.NativeAOT");
	static constexpr int32 CurrentVersion = 1;

	ECSRuntimeKind Kind = ECSRuntimeKind::CoreClrHostFxr;
	FString Library;
	/** Where CoreClrEmbedded reads managed assemblies from. Files when the manifest does not say. */
	ECSAssemblySource AssemblySource = ECSAssemblySource::Files;
	TMap<FString, FString> Properties;
	/** Native libraries that p/invokes by these names must bind to (for runtimes without a probing host). */
	TMap<FString, FString> NativeLibraries;
	/** Platform-native ReadyToRun composite images, keyed by the composite name recorded in component assemblies. */
	TMap<FString, FString> ReadyToRunImages;
	/** File the manifest was read from, for diagnostics. */
	FString SourcePath;

	/**
	 * Reads the manifest from the managed assembly directory. Returns an unset optional when the directory holds
	 * neither a manifest nor the legacy NativeAOT marker. A manifest that exists but cannot be parsed is fatal.
	 */
	static TOptional<FCSRuntimeManifest> Load(const FString& ManagedDirectory);
};
