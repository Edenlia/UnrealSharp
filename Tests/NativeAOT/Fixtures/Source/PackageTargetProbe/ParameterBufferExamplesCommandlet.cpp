#include "ParameterBufferExamplesCommandlet.h"
#include "ParameterBufferTestRunner.h"
#include "Misc/FeedbackContext.h"

UParameterBufferExamplesCommandlet::UParameterBufferExamplesCommandlet()
{
	IsClient = false;
	IsServer = false;
	IsEditor = true;
	LogToConsole = true;
	UseCommandletResultAsExitCode = true;
}

int32 UParameterBufferExamplesCommandlet::Main(const FString& Params)
{
	const int32 Result = RunParameterBufferTests(Params);
	if (Result != 0) return Result;
	// The exception-boundary fixture deliberately logs this error. Preserve the
	// diagnostic while continuing to reject every unexpected commandlet error.
	TArray<FString> Errors;
	GWarn->GetErrors(Errors);
	for (const FString& Error : Errors)
	{
		if (!Error.Contains(TEXT("LogUnrealSharpCore")) ||
			!Error.TrimEnd().EndsWith(TEXT("Exception during InvokeManagedMethod: Expected callback failure: 中文 é")))
		{
			return 4;
		}
	}
	return 0;
}
