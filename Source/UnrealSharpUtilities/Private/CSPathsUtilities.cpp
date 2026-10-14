#include "CSPathsUtilities.h"

#include "CSCommonGlobalSettings.h"
#include "CSDotnetUtilties.h"
#include "CSInstallationUtilities.h"
#include "CSProjectUtilities.h"
#include "Interfaces/IPluginManager.h"
#include "Logging/StructuredLog.h"
#include "Misc/Paths.h"
#include "Dom/JsonObject.h"
#include "HAL/FileManager.h"
#include "Misc/App.h"
#include "Misc/CommandLine.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

namespace
{
    struct FManagedTarget
    {
        FString BindingsRoot;
        FString ManifestPath;
    };

    const FManagedTarget& GetManagedTarget()
    {
        static const FManagedTarget Target = []
        {
            FManagedTarget Result;
            // Staged games have a complete self-contained managed folder, not source-build contexts.
            if (FPaths::FileExists(FPaths::Combine(FPaths::ProjectDir(), UnrealSharp::DotNetUtilities::GetManagedBinaries(), TEXT("UnrealSharpBuild.flag")))) return Result;
            FString RequestedName;
            FParse::Value(FCommandLine::Get(), TEXT("UnrealSharpTarget="), RequestedName);
            const FString TargetType = LexToString(FApp::GetBuildTargetType());
            // Explicit projects keep their original flat runtime directories.
            // A project using a precompiled plugin selects its distribution below.
            if (FPaths::IsProjectFilePathSet() &&
                FPaths::FileExists(FPaths::Combine(FPaths::ProjectDir(), TEXT("Intermediate/UnrealSharp/UHT"), TargetType, TEXT("UnrealSharp.Target.json"))))
            {
                FString ProjectJson;
                TSharedPtr<FJsonObject> ProjectContext;
                if (FFileHelper::LoadFileToString(ProjectJson, *FPaths::Combine(FPaths::ProjectDir(), TEXT("Intermediate/UnrealSharp/UHT"), TargetType, TEXT("UnrealSharp.Target.json"))) &&
                    FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(ProjectJson), ProjectContext) && ProjectContext.IsValid())
                {
                    bool bPrecompiled = false;
                    ProjectContext->TryGetBoolField(TEXT("Precompiled"), bPrecompiled);
                    if (!bPrecompiled) return Result;
                }
            }
#if PLATFORM_WINDOWS
            const FString Platform = TEXT("Win64");
#elif PLATFORM_MAC
            const FString Platform = TEXT("Mac");
#elif PLATFORM_LINUX && PLATFORM_CPU_ARM_FAMILY
            const FString Platform = TEXT("LinuxArm64");
#elif PLATFORM_LINUX
            const FString Platform = TEXT("Linux");
#else
            const FString Platform = FPlatformProperties::IniPlatformName();
#endif
#if PLATFORM_CPU_ARM_FAMILY
            const FString Architecture = TEXT("arm64");
#else
            const FString Architecture = TEXT("x64");
#endif
            const FString Configuration = LexToString(FApp::GetBuildConfiguration());
            const FString FlatBindingsRoot = FPaths::Combine(UnrealSharp::Paths::GetPluginDirectory(), TEXT("Binaries/Managed"));
            const FString Framework = FPaths::GetCleanFilename(UnrealSharp::DotNetUtilities::GetManagedBinaries());
            // A source engine build has the same flat framework outputs as a project build.
            if (!FPaths::IsProjectFilePathSet() && FPaths::FileExists(FlatBindingsRoot / Framework / TEXT("UnrealSharp.dll"))) return Result;
            {
                const FString Directory = FlatBindingsRoot / TEXT("BuildPlugin");
                TArray<FString> Manifests;
                IFileManager::Get().FindFilesRecursive(Manifests, *Directory, TEXT("UnrealSharp.Target.json"), true, false);
                TArray<FString> Matches;
                for (const FString& Manifest : Manifests)
                {
                    FString Json;
                    TSharedPtr<FJsonObject> Object;
                    if (!FFileHelper::LoadFileToString(Json, *Manifest) ||
                        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json), Object) || !Object.IsValid()) continue;
                    FString Name, Type, TargetPlatform, TargetConfiguration, TargetArchitecture;
                    int32 Version = 0;
                    if (!Object->TryGetNumberField(TEXT("Version"), Version) || Version != 3) continue;
                    if (!Object->TryGetStringField(TEXT("Name"), Name) || !Object->TryGetStringField(TEXT("Type"), Type) ||
                        !Object->TryGetStringField(TEXT("Platform"), TargetPlatform) || !Object->TryGetStringField(TEXT("Configuration"), TargetConfiguration) ||
                        !Object->TryGetStringField(TEXT("Architecture"), TargetArchitecture)) continue;
                    TArray<FString> Architectures;
                    TargetArchitecture.ParseIntoArray(Architectures, TEXT("+"));
                    if (Type != TargetType || TargetPlatform != Platform || TargetConfiguration != Configuration ||
                        !Architectures.Contains(Architecture) || (!RequestedName.IsEmpty() && Name != RequestedName)) continue;
                    {
                        bool bDistribution = false;
                        Object->TryGetBoolField(TEXT("Distribution"), bDistribution);
                        if (!bDistribution) continue;
                    }
                    Matches.Add(Manifest);
                }
                if (Matches.Num() > 1)
                {
                    UE_LOG(LogTemp, Fatal, TEXT("Multiple UnrealSharp distributions match %s/%s/%s. Launch with -UnrealSharpTarget=<native target name>."), *TargetType, *Platform, *Configuration);
                }
                if (Matches.Num() == 1)
                {
                    Result.ManifestPath = FPaths::ConvertRelativePathToFull(Matches[0]);
                    Result.BindingsRoot = FPaths::GetPath(Result.ManifestPath);
                    {
                        // BuildPlugin's HostProject no longer exists. Its bundle is
                        // immutable; give this consuming project its own glue outputs.
                        const FString Bundle = Result.BindingsRoot / TEXT("Glue") / Framework;
                        if (!FPaths::FileExists(Bundle / TEXT("GlueCode.LoadOrder.json")))
                            UE_LOG(LogTemp, Fatal, TEXT("Packaged UnrealSharp glue manifest is missing: %s"), *Bundle);
                        const FString Output = FPaths::ConvertRelativePathToFull(FPaths::IsProjectFilePathSet()
                            ? FPaths::ProjectDir() / TEXT("Binaries/Managed") / Framework
                            : Result.BindingsRoot / Framework);
                        IFileManager::Get().MakeDirectory(*Output, true);
                        TArray<FString> Files;
                        IFileManager::Get().FindFiles(Files, *(Bundle / TEXT("*")), true, false);
                        for (const FString& File : Files)
                        {
                            if (IFileManager::Get().Copy(*(Output / File), *(Bundle / File), true, true) != COPY_OK)
                                UE_LOG(LogTemp, Fatal, TEXT("Cannot copy packaged UnrealSharp glue: %s"), *(Bundle / File));
                        }
                    }
                    UE_LOG(LogTemp, Display, TEXT("UnrealSharp distribution: %s (%s/%s/%s)"), *Result.BindingsRoot, *TargetType, *Platform, *Configuration);
                    return Result;
                }
            }
            if (FPaths::FileExists(FlatBindingsRoot / Framework / TEXT("UnrealSharp.dll"))) return Result;
            UE_LOG(LogTemp, Fatal, TEXT("No UnrealSharp managed target matches %s/%s/%s/%s. Build this project's native target first, or install a matching BuildPlugin package."), *TargetType, *Platform, *Configuration, *Architecture);
            return Result;
        }();
        return Target;
    }
}

FString UnrealSharp::Paths::GetPluginDirectory()
{
    TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(UE_PLUGIN_NAME);
    return Plugin->GetBaseDir();
}

FString UnrealSharp::Paths::GetUnrealSharpDirectory()
{
    return FPaths::Combine(GetPluginDirectory(), TEXT("Managed"), TEXT("UnrealSharp"));
}

FString UnrealSharp::Paths::GetPluginAssembliesPath()
{
    if (InstallationUtilities::IsUnrealSharpInstalled())
    {
        return GetUserAssemblyDirectory();
    }
    
    const FString& BindingsRoot = GetManagedTarget().BindingsRoot;
    if (!BindingsRoot.IsEmpty())
    {
        return BindingsRoot / FPaths::GetCleanFilename(DotNetUtilities::GetManagedBinaries());
    }
    return FPaths::Combine(GetPluginDirectory(), DotNetUtilities::GetManagedBinaries());
}

FString UnrealSharp::Paths::GetUnrealSharpPluginsPath()
{
    return GetPluginAssembliesPath() / TEXT("UnrealSharp.Plugins.dll");
}

FString UnrealSharp::Paths::GetUnrealSharpBuildToolPath()
{
#if PLATFORM_WINDOWS || PLATFORM_MAC
    return FPaths::ConvertRelativePathToFull(GetPluginAssembliesPath() / TEXT("UnrealSharpBuildTool.dll"));
#else
    return FPaths::ConvertRelativePathToFull(GetPluginAssembliesPath() / TEXT("UnrealSharpBuildTool"));
#endif
}

FString UnrealSharp::Paths::GetUserAssemblyDirectory()
{
    const FManagedTarget& Target = GetManagedTarget();
    if (!FPaths::IsProjectFilePathSet())
    {
        return FPaths::ConvertRelativePathToFull(Target.BindingsRoot.IsEmpty()
            ? FPaths::Combine(GetPluginDirectory(), DotNetUtilities::GetManagedBinaries())
            : Target.BindingsRoot / FPaths::GetCleanFilename(DotNetUtilities::GetManagedBinaries()));
    }
    return FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::ProjectDir(), DotNetUtilities::GetManagedBinaries()));
}

FString UnrealSharp::Paths::GetManagedTargetManifest()
{
    return GetManagedTarget().ManifestPath;
}

FString UnrealSharp::Paths::GetUnrealSharpMetadataPath()
{
    return FPaths::Combine(GetUserAssemblyDirectory(), TEXT("Assembly.LoadOrder.json"));
}

FString UnrealSharp::Paths::GetGeneratedClassesDirectory()
{
    return FPaths::Combine(GetUnrealSharpDirectory(), TEXT("UnrealSharp"), TEXT("Generated"));
}

const FString& UnrealSharp::Paths::GetScriptFolderDirectory()
{
    static FString ScriptFolderDirectory = FPaths::Combine(FPaths::ProjectDir(), GlobalSettings::Common::GetScriptDirectoryName());
    return ScriptFolderDirectory;
}

const FString& UnrealSharp::Paths::GetPluginsDirectory()
{
    static FString PluginsDirectory = FPaths::Combine(FPaths::ProjectDir(), TEXT("Plugins"));
    return PluginsDirectory;
}

FString UnrealSharp::Paths::GetPathToManagedSolution()
{
    static FString SolutionPath = GetScriptFolderDirectory() / Project::GetUserManagedProjectName() + TEXT(".sln");
    return SolutionPath;
}

FString UnrealSharp::Paths::MakeQuotedPath(const FString& Path)
{
    if (Path.IsEmpty())
    {
        return TEXT("");
    }

    if (Path.StartsWith(TEXT("\"")) && Path.EndsWith(TEXT("\"")))
    {
        return Path;
    }

    return FString::Printf(TEXT("\"%s\""), *Path);
}
