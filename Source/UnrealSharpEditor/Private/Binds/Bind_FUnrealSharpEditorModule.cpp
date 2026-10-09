#include "CSBindsRegistry.h"
#include "CSCompileLog.h"
#include "CSProjectUtilities.h"
#include "HotReload/CSHotReloadSubsystem.h"
#include "Logging/StructuredLog.h"
#include "Types/CSManagedTypeInterface.h"

DECLARE_UNREALSHARP_BINDER(Bind_FUnrealSharpEditorModule)
{
	void InitializeUnrealSharpEditorCallbacks(FCSManagedEditorCallbacks Callbacks)
	{
		FUnrealSharpEditorModule::Get().InitializeManagedEditorCallbacks(Callbacks);
	}

	void GetProjectPaths(TArray<FString>* Paths)
	{
		UnrealSharp::Project::GetAllProjectPaths(*Paths);
	}

	void DirtyUnrealType(UField* Field, ECSTypeStructuralFlags Flags)
	{
		ICSManagedTypeInterface* ManagedTypeInterface = Cast<ICSManagedTypeInterface>(Field);
		ManagedTypeInterface->GetManagedTypeDefinition()->SetDirtyFlags(Flags);
	}
	
	void NotifyNewType()
	{
		UCSHotReloadSubsystem::Get()->NotifyNewType();
	}
	
	// Stage values match the managed IncrementalCompilationManager constants.
	constexpr int32 CompileDiagnosticStageParse = 0;

	void ReportCompileDiagnostic(const UTF16CHAR* Project, const UTF16CHAR* File, int32 Line, int32 Column, const UTF16CHAR* Code, int32 Severity, const UTF16CHAR* Message, const UTF16CHAR* FullText, int32 Stage)
	{
		FCSCompileLogEntry Entry;
		Entry.Source = TEXT("HotReload");
		Entry.Project = FString(Project);
		Entry.File = FString(File);
		Entry.Line = Line;
		Entry.Column = Column;
		Entry.Code = FString(Code);
		Entry.Severity = static_cast<ECSCompileLogSeverity>(FMath::Clamp(Severity, 0, 2));
		Entry.Message = FString(Message);
		Entry.FullText = FString(FullText);

		if (Stage == CompileDiagnosticStageParse)
		{
			FCSCompileLog::Get().AddFileDiagnostic(MoveTemp(Entry));
		}
		else
		{
			FCSCompileLog::Get().AddProjectDiagnostic(MoveTemp(Entry));
		}
	}

	void ClearFileCompileDiagnostics(const UTF16CHAR* File)
	{
		FCSCompileLog::Get().ClearFileDiagnostics(FString(File));
	}

	void BeginProjectCompileDiagnostics(const UTF16CHAR* Project)
	{
		FCSCompileLog::Get().BeginProjectDiagnostics(FString(Project));
	}

	BIND_UNREALSHARP_FUNCTION(InitializeUnrealSharpEditorCallbacks)
	BIND_UNREALSHARP_FUNCTION(GetProjectPaths)
	BIND_UNREALSHARP_FUNCTION(DirtyUnrealType)
	BIND_UNREALSHARP_FUNCTION(NotifyNewType)
	BIND_UNREALSHARP_FUNCTION(ReportCompileDiagnostic)
	BIND_UNREALSHARP_FUNCTION(ClearFileCompileDiagnostics)
	BIND_UNREALSHARP_FUNCTION(BeginProjectCompileDiagnostics)
}
