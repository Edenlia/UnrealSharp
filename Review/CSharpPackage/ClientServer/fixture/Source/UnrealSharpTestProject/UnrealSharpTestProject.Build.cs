using UnrealBuildTool;
public class UnrealSharpTestProject : ModuleRules
{
    public UnrealSharpTestProject(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine" });
    }
}
