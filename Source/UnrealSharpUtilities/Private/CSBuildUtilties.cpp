
#if WITH_EDITOR

#include "CSBuildUtilties.h"
#include "CSBuildActionUtilities.h"
#include "CSPathsUtilities.h"
#include "CSProcessUtilities.h"
#include "CSUnrealSharpUtilitiesSettings.h"
#include "UnrealSharpUtilities.h"
#include "Dom/JsonObject.h"
#include "IUATHelperModule.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/App.h"
#include "Misc/MonitoredProcess.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PluginDescriptor.h"
#include "Serialization/JsonSerializer.h"
#include "Styling/SlateStyleRegistry.h"

#define LOCTEXT_NAMESPACE "UnrealSharpBuildUtilities"

bool UnrealSharp::Build::InvokeUnrealSharpAutomation(const FString& BuildAction, const TMap<FString, FString>* ActionArgs, const FCSCommandError& OnError)
{
	FString Arguments;
	BuildArguments(BuildAction, ActionArgs, Arguments);

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

bool UnrealSharp::Build::EnsurePluginRuntime(const FCSCommandError& OnError)
{
	const TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(UE_PLUGIN_NAME);
	if (!Plugin->GetDescriptor().bInstalled)
	{
		return true;
	}
	if (IsRunningCommandlet())
	{
		// BuildCookRun owns UAT while its cooker runs. Consume the runtime already
		// prepared by UHT or Editor startup instead of launching a nested UAT.
		const FString RuntimeDirectory = Paths::GetPluginAssembliesPath();
		FString Contents;
		TSharedPtr<FJsonObject> Manifest;
		if (FFileHelper::LoadFileToString(Contents, *(RuntimeDirectory / TEXT("UnrealSharp.Runtime.json")))
			&& FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Contents), Manifest)
			&& Manifest.IsValid() && Manifest->HasField(TEXT("UnrealSharp.dll"))
			&& Manifest->HasField(TEXT("UnrealSharp.Plugins.dll")))
		{
			bool bComplete = true;
			for (const auto& Entry : Manifest->Values)
			{
				bComplete &= FPaths::FileExists(RuntimeDirectory / *Entry.Key);
			}
			if (bComplete)
			{
				return true;
			}
		}
		UE_LOG(LogUnrealSharpUtilities, Error, TEXT("The installed UnrealSharp Editor runtime cache is incomplete. Open the Editor or run BuildPluginRuntime before cooking."));
		return false;
	}
	return InvokeUnrealSharpAutomation(BuildAction::BuildPluginRuntime, nullptr, OnError);
}

void UnrealSharp::Build::BuildArguments(const FString& BuildAction, const TMap<FString, FString>* ActionArgs, FString& OutArgs)
{
	const TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(UE_PLUGIN_NAME);
	const FString PluginFolder = FPaths::ConvertRelativePathToFull(Plugin->GetBaseDir());
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
	else if (FPaths::IsUnderDirectory(PluginFolder, EngineFolder))
	{
		// Installed engines discover the packaged Automation record here. UE decides
		// whether to compile; source engines use their normal dependency-tracking records.
		if (FApp::IsEngineInstalled())
		{
			OutArgs += FString::Printf(TEXT(" -ScriptDir=\"%s\""), *PluginFolder);
		}
	}
	else
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
