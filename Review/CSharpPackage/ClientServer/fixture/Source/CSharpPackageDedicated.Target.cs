using UnrealBuildTool;
[SupportedPlatforms(UnrealPlatformClass.Server)]
public class CSharpPackageDedicatedTarget : TargetRules
{
    public CSharpPackageDedicatedTarget(TargetInfo Target) : base(Target)
    {
        Type = TargetType.Server;
        DefaultBuildSettings = BuildSettingsVersion.V7;
        IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_8;
        ExtraModuleNames.Add("UnrealSharpTestProject");
    }
}
