#include "SCSCompileLog.h"

#include "CompileLog/CSCompileLogTab.h"
#include "Framework/MultiBox/MultiBoxBuilder.h"
#include "HAL/PlatformApplicationMisc.h"
#include "Styling/AppStyle.h"
#include "Styling/CoreStyle.h"
#include "Widgets/SNullWidget.h"
#include "Widgets/Images/SImage.h"
#include "Widgets/Input/SCheckBox.h"
#include "Widgets/Input/SMultiLineEditableTextBox.h"
#include "Widgets/Input/SSearchBox.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/Layout/SSplitter.h"
#include "Widgets/Text/STextBlock.h"

#define LOCTEXT_NAMESPACE "SCSCompileLog"

namespace
{
	const FVector2D SeverityIconSize(16.0f, 16.0f);
}

SCSCompileLog::~SCSCompileLog()
{
	FCSCompileLog::Get().OnChanged().Remove(LogChangedHandle);
}

void SCSCompileLog::Construct(const FArguments& InArgs)
{
	ChildSlot
	[
		SNew(SVerticalBox)

		+ SVerticalBox::Slot()
		.AutoHeight()
		[
			MakeToolbar()
		]

		+ SVerticalBox::Slot()
		.FillHeight(1.0f)
		[
			SNew(SSplitter)
			.Orientation(Orient_Vertical)
			.Style(FAppStyle::Get(), "Splitter")
			.PhysicalSplitterHandleSize(2.0f)

			+ SSplitter::Slot()
			.Value(0.72f)
			[
				SNew(SBorder)
				.BorderImage(FAppStyle::GetBrush("Brushes.Recessed"))
				.Padding(0.0f)
				[
					SAssignNew(ListView, SListView<FCSCompileLogEntryPtr>)
					.ListItemsSource(&Rows)
					.SelectionMode(ESelectionMode::Single)
					.OnGenerateRow(this, &SCSCompileLog::OnGenerateRow)
					.OnMouseButtonDoubleClick(this, &SCSCompileLog::OnRowDoubleClicked)
					.OnContextMenuOpening(this, &SCSCompileLog::OnContextMenuOpening)
				]
			]

			+ SSplitter::Slot()
			.Value(0.28f)
			[
				SNew(SBorder)
				.BorderImage(FAppStyle::GetBrush("Brushes.Panel"))
				.Padding(4.0f)
				[
					SNew(SMultiLineEditableTextBox)
					.IsReadOnly(true)
					.AutoWrapText(true)
					.AlwaysShowScrollbars(false)
					.Font(FCoreStyle::GetDefaultFontStyle("Mono", 9))
					.Text(this, &SCSCompileLog::GetDetailsText)
				]
			]
		]
	];

	LogChangedHandle = FCSCompileLog::Get().OnChanged().AddSP(this, &SCSCompileLog::OnLogChanged);
	RebuildRows();
}

TSharedRef<SWidget> SCSCompileLog::MakeToolbar()
{
	return SNew(SBorder)
		.BorderImage(FAppStyle::GetBrush("Brushes.Panel"))
		.Padding(FMargin(4.0f, 2.0f))
		[
			SNew(SHorizontalBox)

			+ SHorizontalBox::Slot()
			.FillWidth(1.0f)
			[
				SNullWidget::NullWidget
			]

			// Search
			+ SHorizontalBox::Slot()
			.AutoWidth()
			.VAlign(VAlign_Center)
			.Padding(4.0f, 0.0f)
			[
				SNew(SBox)
				.WidthOverride(260.0f)
				[
					SNew(SSearchBox)
					.HintText(LOCTEXT("SearchHint", "Search"))
					.DelayChangeNotificationsWhileTyping(true)
					.OnTextChanged_Lambda([this](const FText& InText)
					{
						SearchText = InText;
						RebuildRows();
					})
				]
			]

			// Severity counters
			+ SHorizontalBox::Slot()
			.AutoWidth()
			.VAlign(VAlign_Center)
			[
				MakeSeverityToggle(ECSCompileLogSeverity::Info, "Icons.InfoWithColor", LOCTEXT("InfoTooltip", "Show messages"))
			]

			+ SHorizontalBox::Slot()
			.AutoWidth()
			.VAlign(VAlign_Center)
			.Padding(2.0f, 0.0f, 0.0f, 0.0f)
			[
				MakeSeverityToggle(ECSCompileLogSeverity::Warning, "Icons.WarningWithColor", LOCTEXT("WarningTooltip", "Show warnings"))
			]

			+ SHorizontalBox::Slot()
			.AutoWidth()
			.VAlign(VAlign_Center)
			.Padding(2.0f, 0.0f, 0.0f, 0.0f)
			[
				MakeSeverityToggle(ECSCompileLogSeverity::Error, "Icons.ErrorWithColor", LOCTEXT("ErrorTooltip", "Show errors"))
			]
		];
}

TSharedRef<SWidget> SCSCompileLog::MakeSeverityToggle(ECSCompileLogSeverity Severity, FName IconName, FText ToolTip)
{
	return SNew(SCheckBox)
		.Style(&FAppStyle::Get().GetWidgetStyle<FCheckBoxStyle>("ToggleButtonCheckbox"))
		.ToolTipText(ToolTip)
		.IsChecked_Lambda([this, Severity]() { return IsSeverityVisible(Severity) ? ECheckBoxState::Checked : ECheckBoxState::Unchecked; })
		.OnCheckStateChanged_Lambda([this, Severity](ECheckBoxState State)
		{
			bSeverityVisible[static_cast<int32>(Severity)] = State == ECheckBoxState::Checked;
			RebuildRows();
		})
		[
			SNew(SHorizontalBox)
			+ SHorizontalBox::Slot()
			.AutoWidth()
			.VAlign(VAlign_Center)
			.Padding(4.0f, 2.0f, 3.0f, 2.0f)
			[
				SNew(SImage)
				.Image(FAppStyle::GetBrush(IconName))
				.DesiredSizeOverride(SeverityIconSize)
			]
			+ SHorizontalBox::Slot()
			.AutoWidth()
			.VAlign(VAlign_Center)
			.Padding(0.0f, 2.0f, 4.0f, 2.0f)
			[
				SNew(STextBlock)
				.Text(this, &SCSCompileLog::GetSeverityCountText, Severity)
			]
		];
}

TSharedRef<ITableRow> SCSCompileLog::OnGenerateRow(FCSCompileLogEntryPtr EntryPtr, const TSharedRef<STableViewBase>& OwnerTable)
{
	const FCSCompileLogEntry& Entry = *EntryPtr;

	return SNew(STableRow<FCSCompileLogEntryPtr>, OwnerTable)
		.Style(&FAppStyle::Get().GetWidgetStyle<FTableRowStyle>("TableView.AlternatingRow"))
		.ToolTipText(FText::FromString(Entry.FullText))
		[
			SNew(SHorizontalBox)

			+ SHorizontalBox::Slot()
			.AutoWidth()
			.VAlign(VAlign_Center)
			.Padding(6.0f, 4.0f, 6.0f, 4.0f)
			[
				SNew(SImage)
				.Image(GetSeverityIcon(Entry.Severity))
				.DesiredSizeOverride(SeverityIconSize)
			]

			+ SHorizontalBox::Slot()
			.FillWidth(1.0f)
			.VAlign(VAlign_Center)
			.Padding(0.0f, 4.0f, 6.0f, 4.0f)
			[
				SNew(STextBlock)
				.Text(GetSummaryText(Entry))
				.HighlightText_Lambda([this]() { return SearchText; })
				.OverflowPolicy(ETextOverflowPolicy::Ellipsis)
			]
		];
}

void SCSCompileLog::OnRowDoubleClicked(FCSCompileLogEntryPtr Entry)
{
	if (Entry.IsValid() && Entry->HasLocation())
	{
		UnrealSharp::CompileLog::OpenSourceAt(Entry->File, Entry->Line, Entry->Column);
	}
}

TSharedPtr<SWidget> SCSCompileLog::OnContextMenuOpening()
{
	FCSCompileLogEntryPtr Selected = GetSelectedEntry();
	if (!Selected.IsValid())
	{
		return nullptr;
	}

	FMenuBuilder MenuBuilder(true, nullptr);

	MenuBuilder.AddMenuEntry(
		LOCTEXT("OpenFile", "Open Source File"),
		LOCTEXT("OpenFileTooltip", "Open the file at the reported line"),
		FSlateIcon(FAppStyle::GetAppStyleSetName(), "Icons.FolderOpen"),
		FUIAction(
			FExecuteAction::CreateSP(this, &SCSCompileLog::OpenSelected),
			FCanExecuteAction::CreateLambda([Selected]() { return Selected->HasLocation(); })));

	MenuBuilder.AddMenuEntry(
		LOCTEXT("CopyMessage", "Copy Message"),
		FText::GetEmpty(),
		FSlateIcon(FAppStyle::GetAppStyleSetName(), "GenericCommands.Copy"),
		FUIAction(FExecuteAction::CreateSP(this, &SCSCompileLog::CopySelected, false)));

	MenuBuilder.AddMenuEntry(
		LOCTEXT("CopyFullText", "Copy Full Text"),
		FText::GetEmpty(),
		FSlateIcon(FAppStyle::GetAppStyleSetName(), "GenericCommands.Copy"),
		FUIAction(FExecuteAction::CreateSP(this, &SCSCompileLog::CopySelected, true)));

	return MenuBuilder.MakeWidget();
}

FReply SCSCompileLog::OnKeyDown(const FGeometry& MyGeometry, const FKeyEvent& InKeyEvent)
{
	if (InKeyEvent.GetKey() == EKeys::C && (InKeyEvent.IsControlDown() || InKeyEvent.IsCommandDown()))
	{
		if (GetSelectedEntry().IsValid())
		{
			CopySelected(true);
			return FReply::Handled();
		}
	}

	return SCompoundWidget::OnKeyDown(MyGeometry, InKeyEvent);
}

void SCSCompileLog::OnLogChanged()
{
	RebuildRows();
}

void SCSCompileLog::RebuildRows()
{
	const FCSCompileLogEntryPtr PreviousEntry = GetSelectedEntry();

	Rows.Reset();
	for (const FCSCompileLogEntryPtr& Entry : FCSCompileLog::Get().GetEntries())
	{
		if (PassesFilter(*Entry))
		{
			Rows.Add(Entry);
		}
	}

	if (!ListView.IsValid())
	{
		return;
	}

	ListView->RequestListRefresh();

	if (PreviousEntry.IsValid() && Rows.Contains(PreviousEntry))
	{
		ListView->SetSelection(PreviousEntry, ESelectInfo::Direct);
	}
	else if (Rows.Num() > 0)
	{
		ListView->ScrollToBottom();
	}
}

bool SCSCompileLog::PassesFilter(const FCSCompileLogEntry& Entry) const
{
	if (!IsSeverityVisible(Entry.Severity))
	{
		return false;
	}

	if (SearchText.IsEmpty())
	{
		return true;
	}

	const FString Search = SearchText.ToString();
	return Entry.Message.Contains(Search)
		|| Entry.Code.Contains(Search)
		|| Entry.File.Contains(Search)
		|| Entry.Project.Contains(Search);
}

FText SCSCompileLog::GetSeverityCountText(ECSCompileLogSeverity Severity) const
{
	const int32 Count = FCSCompileLog::Get().GetCount(Severity);
	return Count > 999 ? FText::FromString(TEXT("999+")) : FText::AsNumber(Count);
}

FText SCSCompileLog::GetDetailsText() const
{
	FCSCompileLogEntryPtr Selected = GetSelectedEntry();
	if (!Selected.IsValid())
	{
		return FText::GetEmpty();
	}

	const FCSCompileLogEntry& Entry = *Selected;
	FString Details = Entry.FullText;

	if (Entry.HasLocation())
	{
		Details += FString::Printf(TEXT("\n\n%s(%d,%d)"), *Entry.File, Entry.Line, Entry.Column);
	}

	if (!Entry.Project.IsEmpty())
	{
		Details += FString::Printf(TEXT("\nProject: %s"), *Entry.Project);
	}

	Details += FString::Printf(TEXT("\nSource: %s    %s"), *Entry.Source, *Entry.Time.ToString(TEXT("%Y-%m-%d %H:%M:%S")));
	return FText::FromString(Details);
}

FCSCompileLogEntryPtr SCSCompileLog::GetSelectedEntry() const
{
	if (!ListView.IsValid())
	{
		return nullptr;
	}

	TArray<FCSCompileLogEntryPtr> Selected = ListView->GetSelectedItems();
	return Selected.Num() > 0 ? Selected[0] : nullptr;
}

void SCSCompileLog::CopySelected(bool bFullText) const
{
	if (FCSCompileLogEntryPtr Selected = GetSelectedEntry())
	{
		const FString& Text = bFullText ? Selected->FullText : Selected->Message;
		FPlatformApplicationMisc::ClipboardCopy(*Text);
	}
}

void SCSCompileLog::OpenSelected() const
{
	FCSCompileLogEntryPtr Selected = GetSelectedEntry();
	if (Selected.IsValid() && Selected->HasLocation())
	{
		UnrealSharp::CompileLog::OpenSourceAt(Selected->File, Selected->Line, Selected->Column);
	}
}

FText SCSCompileLog::GetSummaryText(const FCSCompileLogEntry& Entry)
{
	FString FirstLine = Entry.Message;
	int32 NewLineIndex = INDEX_NONE;
	if (FirstLine.FindChar(TEXT('\n'), NewLineIndex))
	{
		FirstLine.LeftInline(NewLineIndex);
		FirstLine.TrimEndInline();
	}

	FString Summary = FString::Printf(TEXT("[%s] "), *Entry.Time.ToString(TEXT("%H:%M:%S")));

	if (!Entry.Code.IsEmpty())
	{
		Summary += Entry.Code + TEXT(" ");
	}

	if (Entry.HasLocation())
	{
		Summary += FString::Printf(TEXT("%s(%d,%d): "), *FPaths::GetCleanFilename(Entry.File), Entry.Line, Entry.Column);
	}

	Summary += FirstLine;
	return FText::FromString(Summary);
}

const FSlateBrush* SCSCompileLog::GetSeverityIcon(ECSCompileLogSeverity Severity)
{
	switch (Severity)
	{
	case ECSCompileLogSeverity::Error:
		return FAppStyle::GetBrush("Icons.ErrorWithColor");
	case ECSCompileLogSeverity::Warning:
		return FAppStyle::GetBrush("Icons.WarningWithColor");
	default:
		return FAppStyle::GetBrush("Icons.InfoWithColor");
	}
}

#undef LOCTEXT_NAMESPACE
