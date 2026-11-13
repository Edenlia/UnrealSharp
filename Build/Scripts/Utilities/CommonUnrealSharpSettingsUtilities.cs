using System.Text.Json;
using AutomationTool;

namespace UnrealSharp.Automation.Utilities;

public static class CommonUnrealSharpSettingsUtilities
{
    public static string GetScriptDirectoryName(this BuildCommand buildCommand)
    {
        JsonElement ScriptDirectoryName = buildCommand.GetElement("ScriptDirectoryName");
        return ScriptDirectoryName.GetString()!;
    }

    public static bool GetTrimFramework(this BuildCommand buildCommand)
    {
        JsonElement TrimFramework = buildCommand.GetElement("TrimFramework");
        return TrimFramework.GetBoolean();
    }
}