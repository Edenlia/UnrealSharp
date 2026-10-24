using UnrealBuildTool;

public class PackageTargetProbe : ModuleRules
{
    public PackageTargetProbe(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PrivateDependencyModuleNames.AddRange(new string[]
        {
            "Core", "CoreUObject", "Engine", "UnrealEd", "UnrealSharpEditor", "PackageTargetFixture"
        });
    }
}
