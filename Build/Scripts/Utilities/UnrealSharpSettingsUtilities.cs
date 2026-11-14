using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AutomationTool;

namespace UnrealSharp.Automation.Utilities;

public static class UnrealSharpSettingsUtilities
{
    private static readonly ConditionalWeakTable<BuildCommand, Dictionary<string, JsonElement>> Configs = new();

    public static void Reset() => Configs.Clear();
    
    static Dictionary<string, JsonElement> InitializeConfigFile(BuildCommand buildCommand)
    {
        string PluginConfigPath = GetConfigFile(buildCommand.GetUnrealSharpRootFolder());
        string ProjectConfigPath = GetConfigFile(buildCommand.GetProjectRootFolder());

        Dictionary<string, JsonElement> Config = LoadJsonAsDictionary(PluginConfigPath);
        Dictionary<string, JsonElement> ProjectDict = File.Exists(ProjectConfigPath) ? LoadJsonAsDictionary(ProjectConfigPath) : new Dictionary<string, JsonElement>();
        
        foreach (KeyValuePair<string, JsonElement> Kvp in ProjectDict)
        {
            Config[Kvp.Key] = Kvp.Value;
        }

        return Config;
    }

    public static JsonElement GetElement(this BuildCommand buildCommand, string elementName)
    {
        return Configs.GetValue(buildCommand, InitializeConfigFile)[elementName];
    }

    public static bool TryGetElement(this BuildCommand buildCommand, string elementName, out JsonElement element)
    {
        return Configs.GetValue(buildCommand, InitializeConfigFile).TryGetValue(elementName, out element);
    }
    
    static string GetConfigFile(string rootDirectory)
    {
        string ConfigDirectory = Path.Combine(rootDirectory, "Config");
        // BuildPlugin's temporary host project has no Config directory. Project
        // settings are optional, so use the plugin defaults when it is absent.
        if (!Directory.Exists(ConfigDirectory))
        {
            return string.Empty;
        }
        
        EnumerationOptions EnumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true
        };

        string[] FoundConfigs = Directory.GetFiles(ConfigDirectory, "UnrealSharp.Settings.json", EnumerationOptions);

        if (FoundConfigs.Length > 1)
        {
            throw new Exception("Found multiple config files");
        }

        if (FoundConfigs.Length == 0)
        {
            return string.Empty;
        }

        return FoundConfigs[0];
    }

    static Dictionary<string, JsonElement> LoadJsonAsDictionary(string path)
    {
        string Json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Json) ?? new Dictionary<string, JsonElement>();
    }
}
