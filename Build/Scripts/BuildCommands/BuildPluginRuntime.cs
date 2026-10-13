using AutomationTool;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharp.Automation.BuildCommands;

[Help("Builds the installed plugin's editor managed runtime in the user cache when needed.")]
public class BuildPluginRuntime : BuildCommand
{
    public override void ExecuteBuild()
    {
        string pluginDirectory = this.GetUnrealSharpRootFolder();
        if (PluginRuntimeUtilities.IsInstalled(pluginDirectory))
        {
            PluginRuntimeUtilities.EnsureEditorRuntime(pluginDirectory);
        }
    }
}
