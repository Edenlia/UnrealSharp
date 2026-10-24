#include "ParameterBufferExamplesCommandlet.h"
#include "ParameterBufferTestRunner.h"

UParameterBufferExamplesCommandlet::UParameterBufferExamplesCommandlet()
{
	IsClient = false;
	IsServer = false;
	IsEditor = true;
	LogToConsole = true;
}

int32 UParameterBufferExamplesCommandlet::Main(const FString& Params)
{
	return RunParameterBufferTests(Params);
}
