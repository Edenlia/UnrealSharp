#pragma once

#include "CoreMinimal.h"

#if UNREALSHARP_WITH_HOSTFXR_HOST

#include "Runtime/CSRuntimeHost.h"

#include <coreclr_delegates.h>
#include <hostfxr.h>

#include "HAL/PlatformProcess.h"

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

/**
 * Starts CoreCLR through hostfxr, either from a shared .NET installation or from a self-contained layout,
 * and resolves InitializeUnrealSharp with load_assembly_and_get_function_pointer. Used by the Editor and by
 * desktop JIT packages (including packages whose assemblies were compiled with ReadyToRun).
 */
class FCSHostFxrRuntimeHost final : public FCSRuntimeHost
{
public:
	virtual ~FCSHostFxrRuntimeHost() override;

	// FCSRuntimeHost interface
	virtual ECSRuntimeKind GetKind() const override { return ECSRuntimeKind::CoreClrHostFxr; }
	virtual FCSRuntimeCapabilities GetCapabilities() const override;

protected:
	virtual FInitializeUnrealSharp StartRuntime() override;
	// End of FCSRuntimeHost interface

private:
	static FCSDotNetLayout ResolveDotNetLayout(const FString& PluginAssemblyPath);

	load_assembly_and_get_function_pointer_fn InitializeHost();
	load_assembly_and_get_function_pointer_fn ConfigureRuntime(const FCSDotNetLayout& Layout) const;
	void ReleaseHostFxr();

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
};

#endif // UNREALSHARP_WITH_HOSTFXR_HOST
