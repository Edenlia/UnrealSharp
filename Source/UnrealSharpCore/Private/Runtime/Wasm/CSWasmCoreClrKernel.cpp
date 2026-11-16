#if defined(__EMSCRIPTEN__) && UNREALSHARP_WITH_EMBEDDED_CORECLR_HOST

#include "CSWasmCoreClrKernel.h"
#include <coreclrhost.h>
#include <host_runtime_contract.h>
#include <atomic>
#include <cinttypes>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <map>
#include <pthread.h>

const void* callhelpers_pinvoke_override(const char*, const char*);
extern "C" void UnrealSharpWasm_RegisterLifecycle(const int32_t* Phase, const int32_t* Result);
extern "C" void UnrealSharpWasm_BeginShutdown();

namespace
{
enum EPhase : int32_t { NotStarted, Starting, Running, Closing, Closed, Failed };
struct FProcessState
{
    // Kept until process exit: host_runtime_contract requires process-lifetime callbacks/context.
    host_runtime_contract Contract{};
    std::map<std::string, std::string> Properties;
    std::string Error;
    void* Handle = nullptr;
    unsigned int DomainId = 0;
    pthread_t Owner{};
    bool Initialized = false;
    std::atomic<int32_t> Phase{NotStarted};
    std::atomic<int32_t> Result{0};
};
FProcessState* State = nullptr;
std::atomic<FProcessState*> PublishedState{nullptr};
static_assert(sizeof(void*) == 8, "This host is the Wasm64 interpreter path.");
static_assert(sizeof(std::atomic<int32_t>) == sizeof(int32_t));
static_assert(std::atomic<int32_t>::is_always_lock_free);

void Fail(int32_t Code, const char* Text)
{
    int32_t Unset = 0;
    if (State->Result.compare_exchange_strong(Unset, Code)) State->Error = Text;
}

size_t HOST_CONTRACT_CALLTYPE GetRuntimeProperty(const char* Key, char* Buffer, size_t Capacity, void* Context)
{
    const auto& Properties = static_cast<FProcessState*>(Context)->Properties;
    const auto Found = Properties.find(Key);
    if (Found == Properties.end()) return static_cast<size_t>(-1);
    const size_t Required = Found->second.size() + 1;
    if (Buffer && Capacity >= Required) std::memcpy(Buffer, Found->second.c_str(), Required);
    return Required;
}

void WriteRuntimeError(const char* Text) { std::fprintf(stderr, "UnrealSharp CoreCLR: %s\n", Text); }

bool AddProperty(const std::string& Key, const std::string& Value)
{
    const auto Existing = State->Properties.find(Key);
    if (Existing != State->Properties.end() && Existing->second != Value)
    {
        Fail(1, "Runtime manifest attempted to replace a host-owned property.");
        return false;
    }
    State->Properties.emplace(Key, Value);
    return true;
}
}

namespace UnrealSharp::WasmCoreClr
{
void* Start(const FStartArguments& Arguments)
{
    // A process owns one CLR; repeated starts are never hot reload.
    if (State) return nullptr;
    State = new FProcessState();
    State->Owner = pthread_self();
    State->Phase.store(Starting, std::memory_order_release);
    PublishedState.store(State, std::memory_order_release);
    UnrealSharpWasm_RegisterLifecycle(reinterpret_cast<const int32_t*>(&State->Phase),
                                     reinterpret_cast<const int32_t*>(&State->Result));

    std::string Tpa;
    bool HasCoreLib = false, HasEntry = false;
    for (const auto& Name : Arguments.AssemblyFiles)
    {
        if (Name.find_first_of("/\\:") != std::string::npos || Name.size() < 5 || Name.substr(Name.size() - 4) != ".dll")
        {
            Fail(1, "Expected flat loose PE DLL names in the verified managed directory.");
            break;
        }
        const std::string Path = Arguments.ManagedDirectory + "/" + Name;
        FILE* File = std::fopen(Path.c_str(), "rb");
        unsigned char Magic[2]{};
        const size_t Count = File ? std::fread(Magic, 1, sizeof(Magic), File) : 0;
        if (File) std::fclose(File);
        if (Count != 2 || Magic[0] != 'M' || Magic[1] != 'Z')
        {
            Fail(1, "A prepared DLL is not a readable loose PE on WasmFS.");
            break;
        }
        if (!Tpa.empty()) Tpa += ':';
        Tpa += Path;
        HasCoreLib |= Name == "System.Private.CoreLib.dll";
        HasEntry |= Name == "UnrealSharp.Plugins.dll";
    }
    if (!HasCoreLib || !HasEntry || State->Result.load() != 0)
    {
        Fail(1, "Complete matching CoreLib/UnrealSharp.Plugins loose PE closure is required.");
        State->Phase.store(Failed, std::memory_order_release);
        return nullptr;
    }

    State->Contract.size = sizeof(host_runtime_contract);
    State->Contract.context = State;
    State->Contract.get_runtime_property = &GetRuntimeProperty;
    State->Contract.pinvoke_override = &callhelpers_pinvoke_override;
    // bundle_probe/external_assembly_probe/get_native_code_data stay null: real PE file fallback, no R2R/bundle fiction.
    char ContractAddress[2 + sizeof(uintptr_t) * 2 + 1];
    std::snprintf(ContractAddress, sizeof(ContractAddress), "0x%" PRIxPTR, reinterpret_cast<uintptr_t>(&State->Contract));
    State->Properties = {
        {HOST_PROPERTY_RUNTIME_CONTRACT, ContractAddress},
        {HOST_PROPERTY_TRUSTED_PLATFORM_ASSEMBLIES, Tpa},
        {HOST_PROPERTY_SYSTEM_CORELIB_DIRECTORY, Arguments.ManagedDirectory},
        {HOST_PROPERTY_APP_PATHS, Arguments.ManagedDirectory},
        {HOST_PROPERTY_ENTRY_ASSEMBLY_NAME, "UnrealSharp.Plugins"},
        {HOST_PROPERTY_ARGV0, Arguments.ExecutablePath},
        {"APP_CONTEXT_BASE_DIRECTORY", Arguments.ManagedDirectory},
        {"UnrealSharp.UseAssemblyDependencyResolver", "false"},
        // The first profile is a synchronous single-managed-thread interpreter. Broader services need actual acceptance.
        {"System.Globalization.Invariant", "true"}, {"System.GC.Concurrent", "false"}, {"System.GC.Server", "false"},
    };
    for (const auto& Property : Arguments.Properties)
    {
        if (!AddProperty(Property.first, Property.second))
        {
            State->Phase.store(Failed, std::memory_order_release);
            return nullptr;
        }
    }
    std::vector<const char*> Keys, Values;
    for (const auto& Property : State->Properties)
    {
        Keys.push_back(Property.first.c_str());
        Values.push_back(Property.second.c_str());
    }
    // Actual CoreCLR switch, not a replacement diagnostic/CLR service.
    setenv("DOTNET_EnableDiagnostics", "0", 1);
    coreclr_set_error_writer(&WriteRuntimeError);
    const int Hr = coreclr_initialize(Arguments.ExecutablePath.c_str(), "UnrealSharp",
        static_cast<int>(Keys.size()), Keys.data(), Values.data(), &State->Handle, &State->DomainId);
    if (Hr < 0 || !State->Handle)
    {
        Fail(2, "coreclr_initialize failed.");
        State->Phase.store(Failed, std::memory_order_release);
        return nullptr;
    }
    State->Initialized = true;
    State->Phase.store(Running, std::memory_order_release);
    void* Entry = nullptr;
    const int DelegateHr = coreclr_create_delegate(State->Handle, State->DomainId,
        "UnrealSharp.Plugins", "UnrealSharp.Plugins.Main", "InitializeUnrealSharp", &Entry);
    if (DelegateHr < 0 || !Entry)
    {
        Fail(3, "coreclr_create_delegate for InitializeUnrealSharp failed.");
        Shutdown();
        return nullptr;
    }
    return Entry;
}

int Shutdown()
{
    FProcessState* Snapshot = PublishedState.load(std::memory_order_acquire);
    if (!Snapshot) return 0;
    if (!pthread_equal(pthread_self(), Snapshot->Owner))
    {
        int32_t Unset = 0;
        Snapshot->Result.compare_exchange_strong(Unset, 4);
        Snapshot->Phase.store(Failed, std::memory_order_release);
        return Snapshot->Result.load();
    }
    if (!State->Initialized) return State->Result.load(std::memory_order_acquire);
    State->Phase.store(Closing, std::memory_order_release);
    UnrealSharpWasm_BeginShutdown(); // same owner JS worker; real production timer cleanup
    int Latched = 0;
    const int Hr = coreclr_shutdown_2(State->Handle, State->DomainId, &Latched);
    State->Initialized = false;
    if (Hr < 0) Fail(5, "coreclr_shutdown_2 failed.");
    else if (Latched != 0) Fail(Latched, "CoreCLR returned a nonzero latched exit code.");
    // Never let shutdown's zero latched code erase an earlier failure.
    State->Phase.store(Hr < 0 ? Failed : Closed, std::memory_order_release);
    return State->Result.load(std::memory_order_acquire);
}

const char* GetError() { return State ? State->Error.c_str() : "CoreCLR was not started."; }
}

// Required by the production Browser library's ordinary Emscripten exit wrapper.
// Observation only: never enter CLR from the main JS thread after an owner-worker startup.
extern "C" int BrowserHost_ShutdownDotnet(int ExitCode)
{
    const FProcessState* Snapshot = PublishedState.load(std::memory_order_acquire);
    if (!Snapshot) return ExitCode;
    const int32_t Phase = Snapshot->Phase.load(std::memory_order_acquire);
    const int32_t Result = Snapshot->Result.load(std::memory_order_acquire);
    if (Phase != Closed && Phase != Failed) return Result != 0 ? Result : 71;
    return Result != 0 ? Result : ExitCode;
}
#endif
