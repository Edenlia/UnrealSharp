using System.IO;
using System.Text.Json;
using AutomationTool;
using UnrealSharp.Automation.BuildCommands;
using UnrealSharp.Automation.Utilities;
public class AmendPluginIdentity : BuildCommand
{
    public override void ExecuteBuild()
    {
        File.WriteAllText(ParseRequiredStringParam("Report"), JsonSerializer.Serialize(new
        {
            Plugin = this.GetUnrealSharpUPlugin().FullName,
            Assembly = typeof(PackageProject).Assembly.Location
        }));
    }
}
