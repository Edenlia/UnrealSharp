namespace UnrealSharp;

/// <summary>
/// Mirrors the registration flags of EConsoleVariableFlags in HAL/IConsoleManager.h.
/// </summary>
[Flags]
public enum EConsoleVariableFlags : uint
{
    /// <summary>No flags are set.</summary>
    Default = 0x0,

    /// <summary>Hidden in the console and can't be changed by the user in a final release build.</summary>
    Cheat = 0x1,

    /// <summary>Can't be changed from the console, only from code or an ini.</summary>
    ReadOnly = 0x4,

    /// <summary>Maintains a render thread shadow copy. Not supported on console commands.</summary>
    RenderThreadSafe = 0x20,

    /// <summary>Controlled by a scalability group. Can't be combined with Cheat or ReadOnly.</summary>
    Scalability = 0x40,

    /// <summary>Controls other Scalability variables; names should start with "sg.".</summary>
    ScalabilityGroup = 0x80,

    /// <summary>Included in the device profile previews.</summary>
    Preview = 0x100,

    /// <summary>Excluded from the device profile previews.</summary>
    ExcludeFromPreview = 0x1000,

    /// <summary>Saved into a local temp file for the next boot after it was hotfixed.</summary>
    SaveForNextBoot = 0x2000,
}

/// <summary>
/// Mirrors the ECVF_SetBy* priorities in HAL/IConsoleManager.h, sorted from weak to strong.
/// A set with a weaker priority than the one that last set the variable is ignored.
/// </summary>
public enum EConsoleVariableSetBy : uint
{
    Constructor = 0x00000000,
    Scalability = 0x02000000,
    GameSetting = 0x03000000,
    ProjectSetting = 0x04000000,
    SystemSettingsIni = 0x05000000,
    PluginLowPriority = 0x06000000,
    DeviceProfile = 0x07000000,
    PluginHighPriority = 0x08000000,
    GameOverride = 0x09000000,
    ConsoleVariablesIni = 0x0A000000,
    Hotfix = 0x0B000000,
    Preview = 0x0C000000,
    Commandline = 0x0D000000,
    Code = 0x0E000000,
    Temp = 0x0F000000,
    Console = 0x10000000,
}
