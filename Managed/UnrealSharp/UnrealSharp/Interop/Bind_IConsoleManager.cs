using UnrealSharp.Binds;
using UnrealSharp.Core;

namespace UnrealSharp.Interop;

[NativeCallbacks]
public static unsafe partial class Bind_IConsoleManager
{
    // name, default value, help, flags, owning assembly name, changed callback, managed id -> IConsoleVariable*
    public static delegate* unmanaged<char*, NativeBool, char*, uint, char*, IntPtr, int, IntPtr> RegisterBoolVariable;
    public static delegate* unmanaged<char*, int, char*, uint, char*, IntPtr, int, IntPtr> RegisterIntVariable;
    public static delegate* unmanaged<char*, float, char*, uint, char*, IntPtr, int, IntPtr> RegisterFloatVariable;
    public static delegate* unmanaged<char*, char*, char*, uint, char*, IntPtr, int, IntPtr> RegisterStringVariable;

    // name, help, flags, owning assembly name, execute callback, managed id -> IConsoleCommand*
    public static delegate* unmanaged<char*, char*, uint, char*, IntPtr, int, IntPtr> RegisterCommand;

    public static delegate* unmanaged<IntPtr, void> Unregister;

    public static delegate* unmanaged<IntPtr, NativeBool> GetBool;
    public static delegate* unmanaged<IntPtr, int> GetInt;
    public static delegate* unmanaged<IntPtr, float> GetFloat;
    public static delegate* unmanaged<IntPtr, UnmanagedArray*, void> GetString;

    public static delegate* unmanaged<IntPtr, NativeBool, uint, void> SetBool;
    public static delegate* unmanaged<IntPtr, int, uint, void> SetInt;
    public static delegate* unmanaged<IntPtr, float, uint, void> SetFloat;
    public static delegate* unmanaged<IntPtr, char*, uint, void> SetString;
}
