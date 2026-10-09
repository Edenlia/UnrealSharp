#pragma once

#include "CoreMinimal.h"
#include "CSCompileLog.h"
#include "Widgets/SCompoundWidget.h"
#include "Widgets/Views/SListView.h"

class ITableRow;
class STableViewBase;

using FCSCompileLogEntryPtr = TSharedPtr<FCSCompileLogEntry>;

// Unity Console-style view of the current C# compile state: unresolved errors and warnings plus the latest compile status.
class SCSCompileLog : public SCompoundWidget
{
public:
	SLATE_BEGIN_ARGS(SCSCompileLog) {}
	SLATE_END_ARGS()

	virtual ~SCSCompileLog() override;

	void Construct(const FArguments& InArgs);

	virtual FReply OnKeyDown(const FGeometry& MyGeometry, const FKeyEvent& InKeyEvent) override;
	virtual bool SupportsKeyboardFocus() const override { return true; }

private:
	TSharedRef<SWidget> MakeToolbar();
	TSharedRef<SWidget> MakeSeverityToggle(ECSCompileLogSeverity Severity, FName IconName, FText ToolTip);

	TSharedRef<ITableRow> OnGenerateRow(FCSCompileLogEntryPtr Entry, const TSharedRef<STableViewBase>& OwnerTable);
	void OnRowDoubleClicked(FCSCompileLogEntryPtr Entry);
	TSharedPtr<SWidget> OnContextMenuOpening();

	void OnLogChanged();
	void RebuildRows();
	bool PassesFilter(const FCSCompileLogEntry& Entry) const;

	bool IsSeverityVisible(ECSCompileLogSeverity Severity) const { return bSeverityVisible[static_cast<int32>(Severity)]; }
	FText GetSeverityCountText(ECSCompileLogSeverity Severity) const;
	FText GetDetailsText() const;

	FCSCompileLogEntryPtr GetSelectedEntry() const;
	void CopySelected(bool bFullText) const;
	void OpenSelected() const;

	static FText GetSummaryText(const FCSCompileLogEntry& Entry);
	static const FSlateBrush* GetSeverityIcon(ECSCompileLogSeverity Severity);

	TSharedPtr<SListView<FCSCompileLogEntryPtr>> ListView;
	TArray<FCSCompileLogEntryPtr> Rows;

	FText SearchText;
	bool bSeverityVisible[3] = { true, true, true };

	FDelegateHandle LogChangedHandle;
};
