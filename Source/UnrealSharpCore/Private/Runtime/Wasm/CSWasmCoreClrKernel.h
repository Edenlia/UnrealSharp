#pragma once

#include <string>
#include <utility>
#include <vector>

// C++ implementation input only, not a serialized Ready/SDK contract.
// The real prepare/stage verifier must freeze these exact loose PE bytes first.
namespace UnrealSharp::WasmCoreClr
{
struct FStartArguments
{
    std::string ManagedDirectory;
    std::string ExecutablePath;
    std::vector<std::string> AssemblyFiles;
    std::vector<std::pair<std::string, std::string>> Properties;
};

void* Start(const FStartArguments& Arguments);
int Shutdown();
const char* GetError();
}
