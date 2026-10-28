using UnrealBuildTool;
public class CSharpPackageEditorTarget : TargetRules
{
    public CSharpPackageEditorTarget(TargetInfo Target) : base(Target)
    {
        Type = TargetType.Editor;
        DefaultBuildSettings = BuildSettingsVersion.V7;
        IncludeOrderVersion = EngineIncludeOrderVersion.Unreal5_8;
        ExtraModuleNames.Add("UnrealSharpTestProject");
    }
}
