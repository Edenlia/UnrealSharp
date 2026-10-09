#include "CSCompileLog.h"

#if WITH_EDITOR

#include "Containers/Ticker.h"
#include "Internationalization/Regex.h"

namespace
{
	constexpr int32 MaxCompileLogEntries = 5000;
}

bool FCSCompileLogEntry::IsSameDiagnostic(const FCSCompileLogEntry& Other) const
{
	return Severity == Other.Severity
		&& Line == Other.Line
		&& Column == Other.Column
		&& Code == Other.Code
		&& File == Other.File
		&& Message == Other.Message;
}

FCSCompileLog& FCSCompileLog::Get()
{
	static FCSCompileLog Instance;
	return Instance;
}

void FCSCompileLog::BeginSession()
{
	check(IsInGameThread());

	SessionErrorCount = 0;
	Messages.Reset();
	NotifyChanged();
}

TSharedPtr<FCSCompileLogEntry> FCSCompileLog::MakeEntry(FCSCompileLogEntry&& Entry)
{
	check(IsInGameThread());

	if (Entry.Time.GetTicks() == 0)
	{
		Entry.Time = FDateTime::Now();
	}

	if (Entry.FullText.IsEmpty())
	{
		Entry.FullText = Entry.Message;
	}

	if (Entry.Severity == ECSCompileLogSeverity::Error)
	{
		++SessionErrorCount;
	}

	return MakeShared<FCSCompileLogEntry>(MoveTemp(Entry));
}

void FCSCompileLog::NotifyChanged()
{
	bViewDirty = true;

	// A compile can report hundreds of diagnostics; notify listeners once per frame.
	if (bBroadcastPending)
	{
		return;
	}

	bBroadcastPending = true;
	FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateLambda([this](float)
	{
		bBroadcastPending = false;
		ChangedEvent.Broadcast();
		return false;
	}));
}

void FCSCompileLog::AddEntry(FCSCompileLogEntry&& Entry)
{
	if (Messages.Num() >= MaxCompileLogEntries)
	{
		Messages.RemoveAt(0, EAllowShrinking::No);
	}

	Messages.Add(MakeEntry(MoveTemp(Entry)));
	NotifyChanged();
}

void FCSCompileLog::ClearFileDiagnostics(const FString& File)
{
	check(IsInGameThread());

	if (FileDiagnostics.Remove(File) > 0)
	{
		NotifyChanged();
	}
}

void FCSCompileLog::AddFileDiagnostic(FCSCompileLogEntry&& Entry)
{
	const FString File = Entry.File;
	FileDiagnostics.FindOrAdd(File).Add(MakeEntry(MoveTemp(Entry)));
	NotifyChanged();
}

void FCSCompileLog::BeginProjectDiagnostics(const FString& Project)
{
	check(IsInGameThread());

	if (ProjectDiagnostics.Remove(Project) > 0)
	{
		NotifyChanged();
	}
}

void FCSCompileLog::AddProjectDiagnostic(FCSCompileLogEntry&& Entry)
{
	const FString Project = Entry.Project;
	ProjectDiagnostics.FindOrAdd(Project).Add(MakeEntry(MoveTemp(Entry)));
	NotifyChanged();
}

const TArray<TSharedPtr<FCSCompileLogEntry>>& FCSCompileLog::GetEntries() const
{
	RebuildView();
	return MergedEntries;
}

void FCSCompileLog::RebuildView() const
{
	if (!bViewDirty)
	{
		return;
	}

	bViewDirty = false;
	MergedEntries.Reset();
	MergedEntries.Append(Messages);

	for (const TPair<FString, FEntryList>& Pair : FileDiagnostics)
	{
		MergedEntries.Append(Pair.Value);
	}

	for (const TPair<FString, FEntryList>& Pair : ProjectDiagnostics)
	{
		MergedEntries.Append(Pair.Value);
	}

	MergedEntries.StableSort([](const TSharedPtr<FCSCompileLogEntry>& A, const TSharedPtr<FCSCompileLogEntry>& B)
	{
		return A->Time < B->Time;
	});

	FMemory::Memzero(Counts);
	for (const TSharedPtr<FCSCompileLogEntry>& Entry : MergedEntries)
	{
		++Counts[static_cast<int32>(Entry->Severity)];
	}
}

int32 FCSCompileLog::GetOutstandingErrorCount() const
{
	int32 ErrorCount = 0;

	auto CountErrors = [&ErrorCount](const TMap<FString, FEntryList>& Diagnostics)
	{
		for (const TPair<FString, FEntryList>& Pair : Diagnostics)
		{
			for (const TSharedPtr<FCSCompileLogEntry>& Entry : Pair.Value)
			{
				ErrorCount += Entry->Severity == ECSCompileLogSeverity::Error ? 1 : 0;
			}
		}
	};

	CountErrors(FileDiagnostics);
	CountErrors(ProjectDiagnostics);
	return ErrorCount;
}

void FCSCompileLog::AddMessage(ECSCompileLogSeverity Severity, const FString& Source, const FString& Message, const FString& FullText)
{
	FCSCompileLogEntry Entry;
	Entry.Severity = Severity;
	Entry.Source = Source;
	Entry.Message = Message;
	Entry.FullText = FullText;
	AddEntry(MoveTemp(Entry));
}

int32 FCSCompileLog::GetCount(ECSCompileLogSeverity Severity) const
{
	RebuildView();
	return Counts[static_cast<int32>(Severity)];
}

int32 FCSCompileLog::ParseMSBuildOutput(const FString& Output, const FString& Source)
{
	// path.cs(line,col): error CS0103: message [project.csproj]
	static const FRegexPattern LocatedPattern(TEXT("((?:[A-Za-z]:)?[\\\\/][^\\r\\n]*?\\.cs)\\((\\d+),(\\d+)\\)\\s*:\\s*(error|warning)\\s+(\\w+)\\s*:\\s*(.*)$"));
	// error MSB1009: message [project.csproj]
	static const FRegexPattern UnlocatedPattern(TEXT("\\b(error|warning)\\s+([A-Z]+\\d+)\\s*:\\s*(.*)$"));

	auto StripProjectSuffix = [](FString Message)
	{
		Message.TrimEndInline();
		if (Message.EndsWith(TEXT("]")))
		{
			int32 OpenIndex = INDEX_NONE;
			if (Message.FindLastChar(TEXT('['), OpenIndex) && OpenIndex > 0)
			{
				Message.LeftInline(OpenIndex);
				Message.TrimEndInline();
			}
		}
		return Message;
	};

	TArray<FString> Lines;
	Output.ParseIntoArrayLines(Lines);

	TArray<FCSCompileLogEntry> Parsed;
	for (const FString& Line : Lines)
	{
		FCSCompileLogEntry Entry;
		Entry.Source = Source;

		FRegexMatcher LocatedMatcher(LocatedPattern, Line);
		if (LocatedMatcher.FindNext())
		{
			Entry.File = FPaths::ConvertRelativePathToFull(LocatedMatcher.GetCaptureGroup(1));
			Entry.Line = FCString::Atoi(*LocatedMatcher.GetCaptureGroup(2));
			Entry.Column = FCString::Atoi(*LocatedMatcher.GetCaptureGroup(3));
			Entry.Severity = LocatedMatcher.GetCaptureGroup(4) == TEXT("error") ? ECSCompileLogSeverity::Error : ECSCompileLogSeverity::Warning;
			Entry.Code = LocatedMatcher.GetCaptureGroup(5);
			Entry.Message = StripProjectSuffix(LocatedMatcher.GetCaptureGroup(6));
		}
		else
		{
			FRegexMatcher UnlocatedMatcher(UnlocatedPattern, Line);
			if (!UnlocatedMatcher.FindNext())
			{
				continue;
			}

			Entry.Severity = UnlocatedMatcher.GetCaptureGroup(1) == TEXT("error") ? ECSCompileLogSeverity::Error : ECSCompileLogSeverity::Warning;
			Entry.Code = UnlocatedMatcher.GetCaptureGroup(2);
			Entry.Message = StripProjectSuffix(UnlocatedMatcher.GetCaptureGroup(3));
		}

		Entry.FullText = Line.TrimStartAndEnd();

		// MSBuild repeats every diagnostic in its final summary.
		const bool bDuplicate = Parsed.ContainsByPredicate([&Entry](const FCSCompileLogEntry& Existing)
		{
			return Existing.IsSameDiagnostic(Entry);
		});

		if (!bDuplicate)
		{
			Parsed.Add(MoveTemp(Entry));
		}
	}

	int32 ErrorCount = 0;
	for (FCSCompileLogEntry& Entry : Parsed)
	{
		if (Entry.Severity == ECSCompileLogSeverity::Error)
		{
			++ErrorCount;
		}

		AddEntry(MoveTemp(Entry));
	}

	return ErrorCount;
}

#endif
