#pragma once

#include "CoreMinimal.h"

#include <coreclr_delegates.h>
#include <hostfxr.h>

#include "HAL/PlatformProcess.h"
#include "Runtime/CSRuntimeTypes.h"

struct FCSDotNetLayout
{
	FString DotNetRoot;
	FString HostFxrPath;
	FString AppAssemblyPath;
	FString RuntimeConfigPath;
	bool bSelfContained = false;

	bool IsValid() const
	{
		return !DotNetRoot.IsEmpty() && !HostFxrPath.IsEmpty() && !RuntimeConfigPath.IsEmpty();
	}
};

class FCSDotNetRuntimeHost
{
public:
	FCSDotNetRuntimeHost() = default;
	~FCSDotNetRuntimeHost();

	bool InitializeManagedRuntime();
	void ShutdownManagedRuntime();
	bool IsNativeAOT() const { return NativeAotLibrary != nullptr; }

private:
	static FCSDotNetLayout ResolveDotNetLayout(const FString& PluginAssemblyPath);
	FInitializeUnrealSharp ResolveJitEntryPoint();

	load_assembly_and_get_function_pointer_fn InitializeHost();
	load_assembly_and_get_function_pointer_fn ConfigureRuntime(const FCSDotNetLayout& Layout) const;

	template <typename FunctionPointer>
	bool BindExport(FunctionPointer& OutFunctionPointer, const TCHAR* ExportName)
	{
		OutFunctionPointer = reinterpret_cast<FunctionPointer>(FPlatformProcess::GetDllExport(RuntimeHost, ExportName));
		return OutFunctionPointer != nullptr;
	}

	hostfxr_initialize_for_dotnet_command_line_fn Hostfxr_InitForCommandLine = nullptr;
	hostfxr_initialize_for_runtime_config_fn Hostfxr_InitForRuntimeConfig = nullptr;
	hostfxr_get_runtime_delegate_fn Hostfxr_GetRuntimeDelegate = nullptr;
	hostfxr_close_fn Hostfxr_Close = nullptr;

	void* RuntimeHost = nullptr;
	// NativeAOT does not support unloading. Keep this library loaded until process exit.
	void* NativeAotLibrary = nullptr;
};
