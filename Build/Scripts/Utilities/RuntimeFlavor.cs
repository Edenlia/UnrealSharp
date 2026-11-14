using System;
using System.Text.Json;
using AutomationTool;
using UnrealBuildTool;
using UnrealSharp.Automation.Utilities.Platform;

namespace UnrealSharp.Automation.Utilities;

/// <summary>
/// The managed runtime a packaged build is published for. The names match the runtime kinds the native
/// plugin reads from UnrealSharp.Runtime.json (ECSRuntimeKind).
/// </summary>
public enum RuntimeFlavor
{
    /// <summary>CoreCLR started through hostfxr with a self-contained .NET layout (JIT).</summary>
    CoreClrHostFxr,
    /// <summary>CoreCLR started directly through coreclr_initialize (iOS: ReadyToRun + interpreter).</summary>
    CoreClrEmbedded,
    /// <summary>A NativeAOT shared library containing all managed code.</summary>
    NativeAot,
}

public static class RuntimeFlavorUtilities
{
    private const string RuntimeSettingName = "Runtime";
    private const string LegacyNativeAotSettingName = "NativeAOT";
    private const string DefaultRuntimeKey = "Default";

    public static bool TryParse(string text, out RuntimeFlavor flavor)
    {
        if (Enum.TryParse(text, ignoreCase: true, out flavor) && Enum.IsDefined(flavor))
        {
            return true;
        }

        // Accept the names people use for the desktop runtime.
        if (text.Equals("Jit", StringComparison.OrdinalIgnoreCase) || text.Equals("CoreClr", StringComparison.OrdinalIgnoreCase))
        {
            flavor = RuntimeFlavor.CoreClrHostFxr;
            return true;
        }

        return false;
    }

    public static RuntimeFlavor Parse(string text)
    {
        if (!TryParse(text, out RuntimeFlavor Flavor))
        {
            throw new AutomationException($"Unknown UnrealSharp runtime '{text}'. Expected one of: {string.Join(", ", Enum.GetNames<RuntimeFlavor>())}.");
        }
        return Flavor;
    }

    /// <summary>
    /// Chooses the runtime for a packaged target. In order: an explicit -Runtime=, the legacy -NativeAOT flag,
    /// the "Runtime" setting (per platform, then "Default"), the legacy "NativeAOT": true setting, and finally
    /// the platform default.
    /// </summary>
    internal static RuntimeFlavor Resolve(BuildCommand command, UnrealTargetPlatform platform, PlatformUtilities platformUtilities,
        string? commandLineRuntime, bool commandLineNativeAot)
    {
        if (!string.IsNullOrEmpty(commandLineRuntime))
        {
            return Parse(commandLineRuntime);
        }

        if (commandLineNativeAot)
        {
            return RuntimeFlavor.NativeAot;
        }

        if (command.TryGetElement(RuntimeSettingName, out JsonElement RuntimeSetting))
        {
            string? Configured = RuntimeSetting.ValueKind switch
            {
                JsonValueKind.String => RuntimeSetting.GetString(),
                JsonValueKind.Object => GetPlatformRuntime(RuntimeSetting, platform),
                _ => throw new AutomationException($"The UnrealSharp \"{RuntimeSettingName}\" setting must be a string or an object keyed by platform."),
            };

            if (!string.IsNullOrEmpty(Configured))
            {
                return Parse(Configured);
            }
        }

        if (command.TryGetElement(LegacyNativeAotSettingName, out JsonElement NativeAotSetting) &&
            NativeAotSetting.ValueKind == JsonValueKind.True)
        {
            return RuntimeFlavor.NativeAot;
        }

        return platformUtilities.DefaultRuntime;
    }

    private static string? GetPlatformRuntime(JsonElement setting, UnrealTargetPlatform platform)
    {
        foreach (JsonProperty Property in setting.EnumerateObject())
        {
            if (Property.Name.Equals(platform.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return Property.Value.GetString();
            }
        }

        return setting.TryGetProperty(DefaultRuntimeKey, out JsonElement Default) ? Default.GetString() : null;
    }
}
