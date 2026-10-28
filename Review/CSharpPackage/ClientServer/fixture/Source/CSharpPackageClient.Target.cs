using UnrealBuildTool;
public class CSharpPackageClientTarget : TargetRules
{
    public CSharpPackageClientTarget(TargetInfo Target) : base(Target)
    {
        Type = TargetType.Client;
        DefaultBuildSettings = BuildSettingsVersion.V7;
        IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_8;
        ExtraModuleNames.Add("UnrealSharpTestProject");
    }
}
