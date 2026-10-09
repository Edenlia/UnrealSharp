#pragma once

#include "CoreMinimal.h"

namespace UnrealSharp::CompileLog
{
	extern const FName TabId;

	void RegisterTab();
	void UnregisterTab();

	UNREALSHARPEDITOR_API void OpenTab();
	// Opens the tab, or brings it to the foreground, and flashes it. Does nothing when it is already the visible tab.
	UNREALSHARPEDITOR_API void RevealTab();
	UNREALSHARPEDITOR_API void OpenSourceAt(const FString& File, int32 Line, int32 Column);
}
