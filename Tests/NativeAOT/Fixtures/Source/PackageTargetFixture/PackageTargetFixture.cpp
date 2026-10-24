#include "PackageTargetFixture.h"
#include "ParameterBufferTestRunner.h"
#include "Misc/CommandLine.h"
#include "Misc/CoreDelegates.h"
#include "Misc/Parse.h"
#include "Modules/ModuleManager.h"

class FPackageTargetFixtureModule : public FDefaultGameModuleImpl
{
public:
	virtual void StartupModule() override
	{
#if !WITH_EDITOR
		if (FParse::Param(FCommandLine::Get(), TEXT("ParameterBufferTests")))
		{
			FCoreDelegates::OnFEngineLoopInitComplete.AddLambda([]()
			{
				const int32 Result = RunParameterBufferTests(FCommandLine::Get());
				FPlatformMisc::RequestExitWithStatus(false, static_cast<uint8>(Result));
			});
		}
#endif
	}
};

IMPLEMENT_PRIMARY_GAME_MODULE(FPackageTargetFixtureModule, PackageTargetFixture, "PackageTargetFixture");
