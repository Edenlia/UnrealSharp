#include "DotNet/CSDotNetRuntimeHost.h"

#if PLATFORM_IOS
#include "UnrealSharpCore.h"
#include "HAL/FileManager.h"
#include "Misc/Paths.h"
#include "DotNet/host_runtime_contract.h"
#include <dlfcn.h>
#include <mach-o/loader.h>

namespace
{
// CoreCLR, its callbacks and the signed R2R image have process lifetime.
host_runtime_contract Contract{};
host_runtime_contract_native_code_data NativeCode{};
TMap<FString, void*> NativeLibraries;
TMap<FString, FString> RuntimeProperties;

size_t GetRuntimeProperty(const char* Key, char* Buffer, size_t BufferSize, void*)
{
    const FString* Value = RuntimeProperties.Find(UTF8_TO_TCHAR(Key));
    if (!Value) return static_cast<size_t>(-1);
    const FTCHARToUTF8 Utf8(**Value);
    const size_t RequiredSize = Utf8.Length() + 1;
    if (Buffer && BufferSize >= RequiredSize) FMemory::Memcpy(Buffer, Utf8.Get(), RequiredSize);
    return RequiredSize;
}

void LogRuntimeError(const char* Message)
{
    UE_LOG(LogUnrealSharp, Error, TEXT("CoreCLR: %s"), UTF8_TO_TCHAR(Message));
}

bool GetNativeCode(const host_runtime_contract_native_code_context* Context, host_runtime_contract_native_code_data* Data)
{
    if (!Context || !Context->owner_composite_name || !Data ||
        FCStringAnsi::Strcmp(Context->owner_composite_name, "UnrealSharpR2R.r2r.dylib") != 0)
        return false;
    *Data = NativeCode;
    static TAtomic<bool> Reported(false);
    if (!Reported.Exchange(true))
    {
        UE_LOG(LogUnrealSharp, Display, TEXT("ReadyToRun: CoreCLR requested the signed composite native code image."));
    }
    return true;
}

const void* ResolvePInvoke(const char* Library, const char* Entry)
{
    FString Name = UTF8_TO_TCHAR(Library);
    Name.RemoveFromEnd(TEXT(".dylib"));
    if (!Name.StartsWith(TEXT("lib"))) Name = TEXT("lib") + Name;
    if (void** Handle = NativeLibraries.Find(Name)) return dlsym(*Handle, Entry);
    return nullptr;
}
}

FInitializeUnrealSharp ResolveIOSReadyToRun(const FString& ManagedDirectory)
{
    const FString Frameworks = FPaths::GetPath(FPlatformProcess::ExecutablePath()) / TEXT("Frameworks");
    TArray<FString> FrameworkDirectories;
    IFileManager::Get().FindFiles(FrameworkDirectories, *(Frameworks / TEXT("*.framework")), false, true);
    for (const FString& Directory : FrameworkDirectories)
    {
        FString Name = FPaths::GetBaseFilename(Directory);
        if (!Name.StartsWith(TEXT("libSystem.")) && Name != TEXT("libcoreclr")) continue;
        const FString Path = Frameworks / Directory / Name;
        void* Handle = dlopen(TCHAR_TO_UTF8(*Path), RTLD_NOW | RTLD_LOCAL);
        if (!Handle)
        {
            UE_LOG(LogUnrealSharp, Error, TEXT("ReadyToRun: cannot load %s: %s"), *Path, UTF8_TO_TCHAR(dlerror()));
            return nullptr;
        }
        NativeLibraries.Add(Name, Handle);
    }
    void** Runtime = NativeLibraries.Find(TEXT("libcoreclr"));
    if (!Runtime) return nullptr;
    const FString CompositePath = Frameworks / TEXT("UnrealSharpR2R.framework/UnrealSharpR2R");
    void* Composite = dlopen(TCHAR_TO_UTF8(*CompositePath), RTLD_NOW | RTLD_LOCAL);
    void* Header = Composite ? dlsym(Composite, "RTR_HEADER") : nullptr;
    Dl_info Info{};
    if (!Header || !dladdr(Header, &Info))
    {
        UE_LOG(LogUnrealSharp, Error, TEXT("ReadyToRun: missing signed composite RTR_HEADER."));
        return nullptr;
    }
    const mach_header_64* Image = static_cast<const mach_header_64*>(Info.dli_fbase);
    if (Image->magic != MH_MAGIC_64) return nullptr;
    const load_command* Command = reinterpret_cast<const load_command*>(Image + 1);
    uint64 ImageSize = 0;
    for (uint32 Index = 0; Index < Image->ncmds; ++Index)
    {
        if (Command->cmd == LC_SEGMENT_64)
        {
            const segment_command_64* Segment = reinterpret_cast<const segment_command_64*>(Command);
            ImageSize = FMath::Max(ImageSize, Segment->vmaddr + Segment->vmsize);
        }
        Command = reinterpret_cast<const load_command*>(reinterpret_cast<const uint8*>(Command) + Command->cmdsize);
    }
    NativeCode = { sizeof(NativeCode), Header, static_cast<size_t>(ImageSize), Info.dli_fbase };
    Contract.size = sizeof(Contract);
    Contract.get_runtime_property = GetRuntimeProperty;
    Contract.get_native_code_data = GetNativeCode;
    Contract.pinvoke_override = ResolvePInvoke;

    using Initialize = int (*)(const char*, const char*, int, const char**, const char**, void**, unsigned int*);
    using CreateDelegate = int (*)(void*, unsigned int, const char*, const char*, const char*, void**);
    using SetErrorWriter = int (*)(void (*)(const char*));
    auto Init = reinterpret_cast<Initialize>(dlsym(*Runtime, "coreclr_initialize"));
    auto Delegate = reinterpret_cast<CreateDelegate>(dlsym(*Runtime, "coreclr_create_delegate"));
    auto ErrorWriter = reinterpret_cast<SetErrorWriter>(dlsym(*Runtime, "coreclr_set_error_writer"));
    if (!Init || !Delegate) return nullptr;
    if (ErrorWriter) ErrorWriter(LogRuntimeError);

    if (!FPaths::FileExists(ManagedDirectory / TEXT("System.Private.CoreLib.dll")))
    {
        UE_LOG(LogUnrealSharp, Error, TEXT("ReadyToRun: missing System.Private.CoreLib.dll in %s"), *ManagedDirectory);
        return nullptr;
    }

    TArray<FString> Assemblies;
    IFileManager::Get().FindFiles(Assemblies, *(ManagedDirectory / TEXT("*.dll")), true, false);
    FString Tpa;
    for (const FString& Assembly : Assemblies) Tpa += ManagedDirectory / Assembly + TEXT(":");
    const FTCHARToUTF8 TpaUtf8(*Tpa), DirectoryUtf8(*ManagedDirectory), ExecutableUtf8(FPlatformProcess::ExecutablePath());
    const FString ContractAddress = FString::Printf(TEXT("0x%llx"), static_cast<unsigned long long>(reinterpret_cast<UPTRINT>(&Contract)));
    const FTCHARToUTF8 ContractUtf8(*ContractAddress);
    const char* Keys[] = { "TRUSTED_PLATFORM_ASSEMBLIES", "APP_PATHS", "APP_CONTEXT_BASE_DIRECTORY", "HOST_RUNTIME_CONTRACT", "System.Runtime.TieredCompilation", "UnrealSharp.ReadyToRun", "SYSTEM_CORELIB_DIRECTORY", "RUNTIME_IDENTIFIER" };
    const char* Values[] = { TpaUtf8.Get(), DirectoryUtf8.Get(), DirectoryUtf8.Get(), ContractUtf8.Get(), "false", "true", DirectoryUtf8.Get(), "ios-arm64" };
    // CoreLib binding reads host properties through the contract, not the AppContext
    // property array. Keep the same values available for the lifetime of CoreCLR.
    for (int32 Index = 0; Index < UE_ARRAY_COUNT(Keys); ++Index)
        RuntimeProperties.Add(UTF8_TO_TCHAR(Keys[Index]), UTF8_TO_TCHAR(Values[Index]));
    UE_LOG(LogUnrealSharp, Display, TEXT("ReadyToRun: initializing CoreCLR with %d assemblies in %s"), Assemblies.Num(), *ManagedDirectory);
    void* Host = nullptr;
    unsigned int Domain = 0;
    int Result = Init(ExecutableUtf8.Get(), "UnrealSharp", UE_ARRAY_COUNT(Keys), Keys, Values, &Host, &Domain);
    if (Result < 0)
    {
        UE_LOG(LogUnrealSharp, Error, TEXT("ReadyToRun: coreclr_initialize failed: 0x%x"), Result);
        return nullptr;
    }
    FInitializeUnrealSharp Entry = nullptr;
    Result = Delegate(Host, Domain, "UnrealSharp.Plugins", "UnrealSharp.Plugins.Main", "InitializeRuntime", reinterpret_cast<void**>(&Entry));
    if (Result < 0 || !Entry)
    {
        UE_LOG(LogUnrealSharp, Error, TEXT("ReadyToRun: coreclr_create_delegate failed: 0x%x"), Result);
        return nullptr;
    }
    UE_LOG(LogUnrealSharp, Display, TEXT("Using iOS CoreCLR ReadyToRun with interpreter fallback: %s"), *CompositePath);
    return Entry;
}
#endif
