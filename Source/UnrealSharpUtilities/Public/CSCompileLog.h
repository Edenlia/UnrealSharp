#pragma once

#include "CoreMinimal.h"

#if WITH_EDITOR

enum class ECSCompileLogSeverity : uint8
{
	Info,
	Warning,
	Error,
};

struct FCSCompileLogEntry
{
	FDateTime Time;
	ECSCompileLogSeverity Severity = ECSCompileLogSeverity::Info;
	// "HotReload" or "Build"
	FString Source;
	FString Project;
	FString File;
	int32 Line = 0;
	int32 Column = 0;
	// Compiler diagnostic id, e.g. CS0103
	FString Code;
	FString Message;
	FString FullText;

	bool HasLocation() const { return !File.IsEmpty(); }
	bool IsSameDiagnostic(const FCSCompileLogEntry& Other) const;
};

// Editor-wide store of C# compiler diagnostics, consumed by the C# Compile Log tab.
// Lives in UnrealSharpUtilities because the startup build runs before UnrealSharpEditor is loaded.
//
// Holds two kinds of entries:
// - Messages: status of the latest compile (completion, unlocated build output, fallback errors). Replaced by each new compile.
// - Current compile state: per-file parse rejections and per-project build/generator/emit diagnostics.
//   They stay until that file or project compiles again, so a later successful compile elsewhere cannot hide them.
class UNREALSHARPUTILITIES_API FCSCompileLog
{
public:
	static FCSCompileLog& Get();

	// Starts a new compile attempt and drops the previous compile's messages.
	void BeginSession();

	void AddEntry(FCSCompileLogEntry&& Entry);
	void AddMessage(ECSCompileLogSeverity Severity, const FString& Source, const FString& Message, const FString& FullText = FString());

	// Diagnostics of a file rejected while parsing, keyed by Entry.File.
	void ClearFileDiagnostics(const FString& File);
	void AddFileDiagnostic(FCSCompileLogEntry&& Entry);

	// Generator and emit diagnostics of a project, keyed by Entry.Project. Begin replaces the project's previous diagnostics.
	void BeginProjectDiagnostics(const FString& Project);
	void AddProjectDiagnostic(FCSCompileLogEntry&& Entry);

	// Drops all file and project diagnostics. Used before a full build, which re-reports everything.
	void ClearAllDiagnostics();

	// Parses MSBuild "file(line,col): error CSxxxx: message [project]" lines. Returns the number of errors found.
	// Located diagnostics with a project become project diagnostics keyed by the .csproj base name, so they stay
	// until hot reload recompiles that project. Everything else is added as a message.
	int32 ParseMSBuildOutput(const FString& Output, const FString& Source);

	// Messages and current diagnostics merged in time order.
	const TArray<TSharedPtr<FCSCompileLogEntry>>& GetEntries() const;
	int32 GetCount(ECSCompileLogSeverity Severity) const;
	int32 GetErrorCountSinceSessionStart() const { return SessionErrorCount; }

	int32 GetOutstandingErrorCount() const;
	bool HasOutstandingErrors() const { return GetOutstandingErrorCount() > 0; }


	FSimpleMulticastDelegate& OnChanged() { return ChangedEvent; }

private:
	using FEntryList = TArray<TSharedPtr<FCSCompileLogEntry>>;

	TSharedPtr<FCSCompileLogEntry> MakeEntry(FCSCompileLogEntry&& Entry);
	void NotifyChanged();
	void RebuildView() const;

	FEntryList Messages;
	TMap<FString, FEntryList> FileDiagnostics;
	TMap<FString, FEntryList> ProjectDiagnostics;

	mutable FEntryList MergedEntries;
	mutable int32 Counts[3] = { 0, 0, 0 };
	mutable bool bViewDirty = true;
	bool bBroadcastPending = false;

	int32 SessionErrorCount = 0;
	FSimpleMulticastDelegate ChangedEvent;
};

#endif
