#include "Runtime/CoreClrEmbedded/CSEmbeddedCoreClrRuntimeHost.h"

#if UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST

#include "CSPlatformUtilities.h"
#include "UnrealSharpCore.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformProcess.h"
#include "Logging/StructuredLog.h"
#include "Misc/App.h"
#include "Misc/ScopeLock.h"
#include "Misc/Paths.h"

#include <coreclrhost.h>
#include <host_runtime_contract.h>

#if PLATFORM_APPLE
#include <dlfcn.h>
#include <mach-o/loader.h>
#endif

using namespace UnrealSharp;

namespace
{
	/** Assembly name, type and method of the managed entry point; the same method the hostfxr host resolves. */
	constexpr const char* EntryAssemblyName = "UnrealSharp.Plugins";
	constexpr const char* EntryTypeName = "UnrealSharp.Plugins.Main";
	constexpr const char* EntryMethodName = "InitializeUnrealSharp";
	/** Symbol the SDK exports from each platform-native ReadyToRun composite image. */
	constexpr const TCHAR* ReadyToRunHeaderSymbol = TEXT("RTR_HEADER");

#if PLATFORM_WINDOWS
	constexpr TCHAR PathListSeparator = TEXT(';');
#else
	constexpr TCHAR PathListSeparator = TEXT(':');
#endif

	struct FReadyToRunImage
	{
		FString CompositeName;
		void* Header = nullptr;
		void* ImageBase = nullptr;
		size_t ImageSize = 0;
	};

	struct FProbedAssembly
	{
		void* Data = nullptr;
		int64 Size = 0;
	};

	/**
	 * State the runtime calls back into through host_runtime_contract. The runtime may use it on any thread until
	 * the process exits and CoreCLR cannot be shut down, so it is allocated once and never freed.
	 */
	struct FEmbeddedRuntimeState
	{
		host_runtime_contract Contract = {};
		FString ManagedDirectory;
		FTCHARToUTF8* ManagedDirectoryUtf8 = nullptr;
		TMap<FString, FString> NativeLibraries;
		TArray<FReadyToRunImage> ReadyToRunImages;

		FCriticalSection LibraryLock;
		TMap<FString, void*> LoadedLibraries;

		/** Probe mode: assembly file names (Foo.dll) mapped to their Unreal file system paths. */
		TMap<FString, FString> ProbeFiles;
		/** Probe mode: assemblies already handed to the runtime. The memory is never freed. */
		TMap<FString, FProbedAssembly> ProbedAssemblies;
		FCriticalSection ProbeLock;
	};

	FEmbeddedRuntimeState* GState = nullptr;

	TArray<ANSICHAR> ToUtf8(const FString& Text)
	{
		const FTCHARToUTF8 Converted(*Text);
		TArray<ANSICHAR> Result(Converted.Get(), Converted.Length());
		Result.Add('\0');
		return Result;
	}

	size_t GetImageSize(void* ImageBase)
	{
#if PLATFORM_APPLE
		// ReadyToRun needs the extent of the mapped image; Mach-O records it as the end of its last segment.
		const mach_header_64* Header = static_cast<const mach_header_64*>(ImageBase);
		if (!Header || Header->magic != MH_MAGIC_64)
		{
			return 0;
		}

		const load_command* Command = reinterpret_cast<const load_command*>(Header + 1);
		size_t ImageSize = 0;
		for (uint32 Index = 0; Index < Header->ncmds; ++Index)
		{
			if (Command->cmd == LC_SEGMENT_64)
			{
				const segment_command_64* Segment = reinterpret_cast<const segment_command_64*>(Command);
				ImageSize = FMath::Max<size_t>(ImageSize, Segment->vmaddr + Segment->vmsize);
			}
			Command = reinterpret_cast<const load_command*>(reinterpret_cast<const uint8*>(Command) + Command->cmdsize);
		}
		return ImageSize;
#else
		return 0;
#endif
	}

	void* GetImageBase(void* Address)
	{
#if PLATFORM_APPLE
		Dl_info Info;
		return dladdr(Address, &Info) ? Info.dli_fbase : nullptr;
#else
		return nullptr;
#endif
	}

	void* LoadManifestLibrary(const FString& LibraryReference)
	{
		const FString LibraryPath = PlatformUtilities::ResolveBundledLibraryPath(GState->ManagedDirectory, LibraryReference);
		void* Library = PlatformUtilities::LoadBundledLibrary(LibraryPath);
		if (!Library)
		{
			const TOptional<FString> LoadError = PlatformUtilities::GetBundledLibraryLoadError();
			UE_LOGFMT(LogUnrealSharp, Error, "Failed to load {0}: {1}", LibraryPath, LoadError.Get(TEXT("unknown error")));
		}
		return Library;
	}

	const void* HOST_CONTRACT_CALLTYPE OverridePInvoke(const char* LibraryName, const char* EntryPointName)
	{
		// P/invokes into the runtime's own native libraries (libSystem.Native, ...) bind to the packaged copies;
		// anything else falls back to the runtime's normal probing.
		FString Name = UTF8_TO_TCHAR(LibraryName);
		for (const TCHAR* Extension : { TEXT(".dylib"), TEXT(".so"), TEXT(".dll") })
		{
			Name.RemoveFromEnd(Extension);
		}

		const FString* LibraryReference = GState->NativeLibraries.Find(Name);
		if (!LibraryReference)
		{
			return nullptr;
		}

		void* Library = nullptr;
		{
			FScopeLock Lock(&GState->LibraryLock);
			if (void** Loaded = GState->LoadedLibraries.Find(Name))
			{
				Library = *Loaded;
			}
			else
			{
				Library = LoadManifestLibrary(*LibraryReference);
				GState->LoadedLibraries.Add(Name, Library);
			}
		}

		if (!Library)
		{
			return nullptr;
		}

		const FString EntryPoint = UTF8_TO_TCHAR(EntryPointName);
		const void* Function = PlatformUtilities::GetBundledLibraryExport(Library, *EntryPoint);
		if (!Function)
		{
			UE_LOGFMT(LogUnrealSharp, Error, "Unable to resolve p/invoke '{0}' in '{1}'", EntryPoint, Name);
		}
		return Function;
	}

	bool HOST_CONTRACT_CALLTYPE GetNativeCodeData(const host_runtime_contract_native_code_context* Context,
		host_runtime_contract_native_code_data* Data)
	{
		if (!Context || !Data || !Context->owner_composite_name)
		{
			return false;
		}

		const FString CompositeName = UTF8_TO_TCHAR(Context->owner_composite_name);
		const FReadyToRunImage* Image = GState->ReadyToRunImages.FindByPredicate(
			[&CompositeName](const FReadyToRunImage& Candidate) { return Candidate.CompositeName == CompositeName; });
		if (!Image)
		{
			return false;
		}

		Data->size = sizeof(host_runtime_contract_native_code_data);
		Data->r2r_header_ptr = Image->Header;
		Data->image_size = Image->ImageSize;
		Data->image_base = Image->ImageBase;
		return true;
	}

	bool HOST_CONTRACT_CALLTYPE ProbeBundle(const char*, int64_t*, int64_t*, int64_t*)
	{
		// Nothing is bundled into a single file. Registering a probe still marks the app as bundled, which the
		// runtime requires before it consults BUNDLE_EXTRACTION_PATH to locate System.Private.CoreLib.
		return false;
	}

	size_t HOST_CONTRACT_CALLTYPE GetRuntimeProperty(const char* Key, char* ValueBuffer, size_t ValueBufferSize, void*)
	{
		if (FCStringAnsi::Strcmp(Key, HOST_PROPERTY_BUNDLE_EXTRACTION_PATH) != 0)
		{
			return static_cast<size_t>(-1);
		}

		const size_t Required = GState->ManagedDirectoryUtf8->Length() + 1;
		if (ValueBuffer && ValueBufferSize >= Required)
		{
			FMemory::Memcpy(ValueBuffer, GState->ManagedDirectoryUtf8->Get(), Required);
		}
		return Required;
	}

	bool HOST_CONTRACT_CALLTYPE ProbeAssembly(const char* Path, void** DataStart, int64_t* Size)
	{
		if (!Path || !DataStart || !Size)
		{
			return false;
		}

		// The runtime asks for "<Simple>.dll" when binding by name, and "<directory>/<file>" for composite images.
		FString FileName = FPaths::GetCleanFilename(UTF8_TO_TCHAR(Path));
		if (!FileName.EndsWith(TEXT(".dll"), ESearchCase::IgnoreCase) && !GState->ProbeFiles.Contains(FileName))
		{
			FileName += TEXT(".dll");
		}

		// Called from any thread that binds an assembly.
		FScopeLock Lock(&GState->ProbeLock);
		if (const FProbedAssembly* Probed = GState->ProbedAssemblies.Find(FileName))
		{
			*DataStart = Probed->Data;
			*Size = Probed->Size;
			return true;
		}

		const FString* FilePath = GState->ProbeFiles.Find(FileName);
		if (!FilePath)
		{
			return false;
		}

		TUniquePtr<FArchive> Reader(IFileManager::Get().CreateFileReader(**FilePath));
		const int64 FileSize = Reader ? Reader->TotalSize() : -1;
		if (FileSize <= 0)
		{
			UE_LOGFMT(LogUnrealSharp, Error, "Failed to read assembly {0}", *FilePath);
			return false;
		}

		// The runtime keeps using the image for the rest of the process, and may write to it.
		FProbedAssembly Probed;
		Probed.Data = FMemory::Malloc(FileSize);
		Probed.Size = FileSize;
		Reader->Serialize(Probed.Data, FileSize);
		if (!Reader->Close())
		{
			UE_LOGFMT(LogUnrealSharp, Error, "Failed to read assembly {0}", *FilePath);
			FMemory::Free(Probed.Data);
			return false;
		}

		GState->ProbedAssemblies.Add(FileName, Probed);
		*DataStart = Probed.Data;
		*Size = Probed.Size;
		return true;
	}

	void CORECLR_CALLING_CONVENTION WriteRuntimeError(const char* Message)
	{
		const FString Text = UTF8_TO_TCHAR(Message);
		UE_LOGFMT(LogUnrealSharp, Error, "CoreCLR: {0}", Text);
	}

	bool LoadReadyToRunImages(const TMap<FString, FString>& Images)
	{
		for (const TPair<FString, FString>& Entry : Images)
		{
			void* Library = LoadManifestLibrary(Entry.Value);
			if (!Library)
			{
				return false;
			}

			FReadyToRunImage Image;
			Image.CompositeName = Entry.Key;
			Image.Header = PlatformUtilities::GetBundledLibraryExport(Library, ReadyToRunHeaderSymbol);
			Image.ImageBase = Image.Header ? GetImageBase(Image.Header) : nullptr;
			Image.ImageSize = Image.ImageBase ? GetImageSize(Image.ImageBase) : 0;
			if (!Image.Header || !Image.ImageBase || Image.ImageSize == 0)
			{
				UE_LOGFMT(LogUnrealSharp, Error, "ReadyToRun image {0} ({1}) has no usable {2} header", Entry.Key, Entry.Value, ReadyToRunHeaderSymbol);
				return false;
			}

			UE_LOGFMT(LogUnrealSharp, Log, "ReadyToRun image {0}: {1} bytes", Entry.Key, static_cast<uint64>(Image.ImageSize));
			GState->ReadyToRunImages.Add(MoveTemp(Image));
		}
		return true;
	}

	FString BuildTrustedPlatformAssemblies(const FString& VirtualDirectory, const FString& PhysicalDirectory)
	{
		TArray<FString> Assemblies;
		IFileManager::Get().FindFiles(Assemblies, *(VirtualDirectory / TEXT("*.dll")), true, false);
		Assemblies.Sort();

		FString Result;
		for (const FString& Assembly : Assemblies)
		{
			if (!Result.IsEmpty())
			{
				Result.AppendChar(PathListSeparator);
			}
			Result += PhysicalDirectory / Assembly;
		}
		return Result;
	}

	void IndexProbeAssemblies(const FString& VirtualDirectory)
	{
		TArray<FString> Assemblies;
		IFileManager::Get().FindFiles(Assemblies, *(VirtualDirectory / TEXT("*.dll")), true, false);
		for (const FString& Assembly : Assemblies)
		{
			GState->ProbeFiles.Add(Assembly, VirtualDirectory / Assembly);
		}
	}

	/** A writable absolute directory for AppContext.BaseDirectory when assemblies have no directory of their own. */
	FString GetProbeBaseDirectory()
	{
		FString Directory = IFileManager::Get().ConvertToAbsolutePathForExternalAppForWrite(*FPaths::ProjectSavedDir());
		IFileManager::Get().MakeDirectory(*Directory, true);
		if (!Directory.EndsWith(TEXT("/")))
		{
			Directory.AppendChar(TEXT('/'));
		}
		return Directory;
	}
}

FCSEmbeddedCoreClrRuntimeHost::FCSEmbeddedCoreClrRuntimeHost(FCSRuntimeManifest InManifest, FString InManagedDirectory)
	: Manifest(MoveTemp(InManifest))
	, ManagedDirectory(MoveTemp(InManagedDirectory))
{
}

FCSRuntimeCapabilities FCSEmbeddedCoreClrRuntimeHost::GetCapabilities() const
{
	FCSRuntimeCapabilities Capabilities;
	Capabilities.bCanLoadAssembliesFromPath = true;
	// ReadyToRun code is ignored in collectible contexts, and unloading has not been validated without hostfxr.
	Capabilities.bSupportsCollectibleLoadContexts = false;
	Capabilities.bSupportsHotReload = false;
	Capabilities.bAssembliesArePrelinked = false;
	return Capabilities;
}

FInitializeUnrealSharp FCSEmbeddedCoreClrRuntimeHost::StartRuntime()
{
	check(!GState);
	if (Manifest.Library.IsEmpty())
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "{0} does not name the CoreCLR library to load.", Manifest.SourcePath);
		return nullptr;
	}

	GState = new FEmbeddedRuntimeState();
	GState->ManagedDirectory = ManagedDirectory;
	const FString PhysicalDirectory = PlatformUtilities::GetManagedWorkingDirectory(ManagedDirectory);
	GState->ManagedDirectoryUtf8 = new FTCHARToUTF8(*PhysicalDirectory);
	GState->NativeLibraries = Manifest.NativeLibraries;

	void* CoreClr = LoadManifestLibrary(Manifest.Library);
	if (!CoreClr)
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "Failed to load the CoreCLR library {0}.", Manifest.Library);
		return nullptr;
	}

	coreclr_initialize_ptr Initialize = reinterpret_cast<coreclr_initialize_ptr>(PlatformUtilities::GetBundledLibraryExport(CoreClr, TEXT("coreclr_initialize")));
	coreclr_create_delegate_ptr CreateDelegate = reinterpret_cast<coreclr_create_delegate_ptr>(PlatformUtilities::GetBundledLibraryExport(CoreClr, TEXT("coreclr_create_delegate")));
	coreclr_set_error_writer_ptr SetErrorWriter = reinterpret_cast<coreclr_set_error_writer_ptr>(PlatformUtilities::GetBundledLibraryExport(CoreClr, TEXT("coreclr_set_error_writer")));
	if (!Initialize || !CreateDelegate)
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "{0} does not export the CoreCLR hosting API.", Manifest.Library);
		return nullptr;
	}

	if (SetErrorWriter)
	{
		SetErrorWriter(&WriteRuntimeError);
	}

	if (!LoadReadyToRunImages(Manifest.ReadyToRunImages))
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "Failed to load the ReadyToRun images listed in {0}.", Manifest.SourcePath);
		return nullptr;
	}

	host_runtime_contract& Contract = GState->Contract;
	Contract.size = sizeof(host_runtime_contract);
	Contract.pinvoke_override = &OverridePInvoke;

	// Properties the host computes; runtimeconfig.json properties from the manifest may not replace them.
	TMap<FString, FString> Properties;
	if (Manifest.AssemblySource == ECSAssemblySource::Probe)
	{
		// Every assembly, System.Private.CoreLib included, comes from the probe; the runtime asks it before
		// consulting the (empty) trusted platform assembly list. Nothing needs a physical managed directory.
		IndexProbeAssemblies(ManagedDirectory);
		Contract.external_assembly_probe = &ProbeAssembly;
		Properties.Add(TEXT("APP_CONTEXT_BASE_DIRECTORY"), GetProbeBaseDirectory());
		UE_LOGFMT(LogUnrealSharp, Log, "Probing {0} assemblies in {1}", GState->ProbeFiles.Num(), ManagedDirectory);
	}
	else
	{
		Contract.get_runtime_property = &GetRuntimeProperty;
		Contract.bundle_probe = &ProbeBundle;
		Contract.get_native_code_data = &GetNativeCodeData;
		Properties.Add(TEXT(HOST_PROPERTY_TRUSTED_PLATFORM_ASSEMBLIES), BuildTrustedPlatformAssemblies(ManagedDirectory, PhysicalDirectory));
		Properties.Add(TEXT(HOST_PROPERTY_APP_PATHS), PhysicalDirectory);
		Properties.Add(TEXT("APP_CONTEXT_BASE_DIRECTORY"), PhysicalDirectory);
		Properties.Add(TEXT(HOST_PROPERTY_NATIVE_DLL_SEARCH_DIRECTORIES), PhysicalDirectory + PathListSeparator);
		Properties.Add(TEXT(HOST_PROPERTY_SYSTEM_CORELIB_DIRECTORY), PhysicalDirectory);
	}
	Properties.Add(TEXT(HOST_PROPERTY_RUNTIME_CONTRACT), FString::Printf(TEXT("0x%llx"), static_cast<uint64>(reinterpret_cast<UPTRINT>(&Contract))));
	Properties.Add(TEXT("UnrealSharp.UseAssemblyDependencyResolver"), TEXT("false"));
	for (const TPair<FString, FString>& Property : Manifest.Properties)
	{
		if (Properties.Contains(Property.Key))
		{
			UE_LOGFMT(LogUnrealSharp, Warning, "Ignoring runtime property {0} from {1}; the host sets it.", Property.Key, Manifest.SourcePath);
			continue;
		}
		Properties.Add(Property.Key, Property.Value);
	}

	TArray<TArray<ANSICHAR>> Storage;
	TArray<const char*> Keys;
	TArray<const char*> Values;
	Storage.Reserve(Properties.Num() * 2);
	for (const TPair<FString, FString>& Property : Properties)
	{
		Keys.Add(Storage.Add_GetRef(ToUtf8(Property.Key)).GetData());
		Values.Add(Storage.Add_GetRef(ToUtf8(Property.Value)).GetData());
	}

#if PLATFORM_ANDROID
	// Android has no executable path (FPlatformProcess::ExecutablePath is fatal there); like dotnet/android, name the app.
	const TArray<ANSICHAR> ExecutablePath = ToUtf8(FApp::GetProjectName());
#else
	const TArray<ANSICHAR> ExecutablePath = ToUtf8(FPlatformProcess::ExecutablePath());
#endif
	void* HostHandle = nullptr;
	unsigned int DomainId = 0;
	const int Result = Initialize(ExecutablePath.GetData(), "UnrealSharp", Keys.Num(), Keys.GetData(), Values.GetData(), &HostHandle, &DomainId);
	if (Result < 0)
	{
		UE_LOGFMT(LogUnrealSharp, Fatal, "coreclr_initialize failed with HRESULT 0x{0}.", FString::Printf(TEXT("%08x"), static_cast<uint32>(Result)));
		return nullptr;
	}

	void* EntryPoint = nullptr;
	const int DelegateResult = CreateDelegate(HostHandle, DomainId, EntryAssemblyName, EntryTypeName, EntryMethodName, &EntryPoint);
	if (DelegateResult < 0 || !EntryPoint)
	{
		const FString EntryType = UTF8_TO_TCHAR(EntryTypeName);
		const FString EntryMethod = UTF8_TO_TCHAR(EntryMethodName);
		UE_LOGFMT(LogUnrealSharp, Fatal, "coreclr_create_delegate for {0}.{1} failed with HRESULT 0x{2}.",
			EntryType, EntryMethod, FString::Printf(TEXT("%08x"), static_cast<uint32>(DelegateResult)));
		return nullptr;
	}

	UE_LOGFMT(LogUnrealSharp, Display, "Started embedded CoreCLR from {0} with {1} ReadyToRun image(s).", Manifest.Library, GState->ReadyToRunImages.Num());
	return reinterpret_cast<FInitializeUnrealSharp>(EntryPoint);
}

#endif // UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST
