
#if WITH_EDITOR

#include "CSBuildUtilties.h"
#include "CSBuildActionUtilities.h"
#include "CSPathsUtilities.h"
#include "CSProcessUtilities.h"
#include "CSUnrealSharpUtilitiesSettings.h"
#include "IUATHelperModule.h"
#include "HAL/FileManager.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/Guid.h"
#include "Misc/MonitoredProcess.h"
#include "Misc/ScopeExit.h"
#include "Styling/SlateStyleRegistry.h"

#define LOCTEXT_NAMESPACE "UnrealSharpBuildUtilities"

bool UnrealSharp::Build::InvokeUnrealSharpAutomation(const FString& BuildAction, const TMap<FString, FString>* ActionArgs, const FCSCommandError& OnError)
{
	FString Arguments;
	BuildArguments(BuildAction, ActionArgs, Arguments);

	TMap<FString, FString> PreviousEnvironment;
	ON_SCOPE_EXIT
	{
		for (const auto& [Name, Value] : PreviousEnvironment)
		{
			FPlatformMisc::SetEnvironmentVar(*Name, *Value);
		}
	};

	if (!FPlatformMisc::GetEnvironmentVariable(TEXT("uebp_EngineSavedFolder")).IsEmpty())
	{
		// UAT may launch this Editor/commandlet. Reuse its compiled scripts rather
		// than rebuilding assemblies that the parent process is still using.
		Arguments += TEXT(" -NoCompileUAT -NoCompile");

		const FString LogDirectory = FPaths::ConvertRelativePathToFull(FPaths::Combine(
			FPaths::ProjectLogDir(), TEXT("UnrealSharpUAT"), FGuid::NewGuid().ToString(EGuidFormats::Digits)));
		IFileManager::Get().MakeDirectory(*LogDirectory, true);

		// Match ProcessUtils.RunUAT's child-instance contract, including separate
		// log files and restoring the host environment after this synchronous call.
		TMap<FString, FString> ChildEnvironment = {
			{TEXT("uebp_UATChildInstance"), TEXT("1")},
			{TEXT("uebp_UATMutexNoWait"), TEXT("1")},
			{TEXT("uebp_LogFolder"), LogDirectory},
			{TEXT("uebp_FinalLogFolder"), LogDirectory}
		};
		if (FPlatformMisc::GetEnvironmentVariable(TEXT("IsBuildMachine")) != TEXT("1"))
		{
			ChildEnvironment.Add(TEXT("uebp_LOCAL_ROOT"), FString());
		}
		for (const auto& [Name, Value] : ChildEnvironment)
		{
			PreviousEnvironment.Add(Name, FPlatformMisc::GetEnvironmentVariable(*Name));
			FPlatformMisc::SetEnvironmentVar(*Name, *Value);
		}
	}

	int32 ReturnCode = 0;
	FString Output;
	return Process::InvokeCommand(FSerializedUATProcess::GetUATPath(), Arguments, ReturnCode, Output, nullptr, OnError);
}

void UnrealSharp::Build::InvokeUnrealSharpAutomation_Async(const FString& BuildAction, const FText& BuildActionDisplayName, const TMap<FString, FString>* ActionArgs, const IUATHelperModule::UatTaskResultCallack& ResultCallback)
{
	if (!IsValid(GEditor))
	{
		if (InvokeUnrealSharpAutomation(BuildAction, ActionArgs))
		{
			 ResultCallback(TEXT("Completed"), 0.0);
		}
		else
		{
			 ResultCallback(TEXT("Failed"), -1.0);
		}
		
		return;
	}
	
	FString Arguments;
	BuildArguments(BuildAction, ActionArgs, Arguments);
	
#if PLATFORM_WINDOWS
	FText PlatformName = LOCTEXT("PlatformName_Windows", "Windows");
#elif PLATFORM_MAC
	FText PlatformName = LOCTEXT("PlatformName_Mac", "Mac");
#elif PLATFORM_LINUX
	FText PlatformName = LOCTEXT("PlatformName_Linux", "Linux");
#else
	FText PlatformName = LOCTEXT("PlatformName_Other", "Other OS");
#endif
	
	IUATHelperModule::Get().CreateUatTask(Arguments, 
		PlatformName, 
		BuildActionDisplayName,
	BuildActionDisplayName, 
	GetBuildActionIcon(),
	nullptr, 
	ResultCallback);
}

bool UnrealSharp::Build::BuildUserSolution(const FCSCommandError& OnError)
{
	FString SolutionDirectory = FPaths::ConvertRelativePathToFull(Paths::GetScriptFolderDirectory());
	
	TMap<FString, FString> Arguments;
	Arguments.Add(TEXT("OutputPath"), Paths::MakeQuotedPath(Paths::GetUserAssemblyDirectory()));
	Arguments.Add(TEXT("TargetConfiguration"), LexToString(FApp::GetBuildConfiguration()));
	
	if (!GetDefault<UCSUnrealSharpUtilitiesSettings>()->bShowBuildWarnings)
	{
		Arguments.Add(TEXT("clp"), TEXT("ErrorsOnly"));
	}

	return InvokeUnrealSharpAutomation(BuildAction::BuildUserSolution, &Arguments, OnError);
}

void UnrealSharp::Build::BuildArguments(const FString& BuildAction, const TMap<FString, FString>* ActionArgs, FString& OutArgs)
{
	const FString PluginFolder = FPaths::ConvertRelativePathToFull(IPluginManager::Get().FindPlugin(UE_PLUGIN_NAME)->GetBaseDir());
	const FString ProjectFolder = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir());
	const FString EngineFolder = FPaths::ConvertRelativePathToFull(FPaths::EngineDir());
	const FString ProjectFile = FPaths::ConvertRelativePathToFull(FPaths::GetProjectFilePath());
	
	OutArgs.Reset();
	OutArgs += BuildAction;
	if (FPaths::IsUnderDirectory(PluginFolder, ProjectFolder))
	{
		// Use the same discovery and build-record root as -ScriptsForProject.
		// Reading the legacy Build/Scripts root too can load duplicate records.
		OutArgs += FString::Printf(TEXT(" -ScriptDir=\"%s\""), *ProjectFolder);
	}
	else if (!FPaths::IsUnderDirectory(PluginFolder, EngineFolder))
	{
		// External plugins are not covered by the default engine/project roots.
		OutArgs += FString::Printf(TEXT(" -ScriptDir=\"%s\""), *FPaths::Combine(PluginFolder, TEXT("Build"), TEXT("Scripts")));
	}
	// -Project selects the command target; -ScriptsForProject also scopes UAT script discovery.
	OutArgs += FString::Printf(TEXT(" -ScriptsForProject=\"%s\" -Project=\"%s\""), *ProjectFile, *ProjectFile);
	
	if (ActionArgs && ActionArgs->Num() > 0)
	{
		for (const auto& [Key, Value] : *ActionArgs)
		{
			OutArgs += FString::Printf(TEXT(" -%s=%s"), *Key, *Value);
		}
	}
}

const FSlateBrush* UnrealSharp::Build::GetBuildActionIcon()
{
	const ISlateStyle* Style = FSlateStyleRegistry::FindSlateStyle("UnrealSharpStyle");
	return Style->GetBrush("UnrealSharp.Toolbar");
}

#endif

#undef LOCTEXT_NAMESPACE
