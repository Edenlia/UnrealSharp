using System;
using UnrealSharp.Binds;

namespace UnrealSharp.Interop;

// Candidate destination: Managed/UnrealSharp/UnrealSharp/Interop, where the
// plugin's existing NativeCallbacks generator supplies Call* wrappers.
[NativeCallbacks]
public static unsafe partial class Bind_BalloonRuntime
{
    public static delegate* unmanaged<IntPtr, IntPtr> GameInstance;
    public static delegate* unmanaged<IntPtr, IntPtr> GameMode;
    public static delegate* unmanaged<IntPtr, int, IntPtr> PlayerPawn;
    public static delegate* unmanaged<IntPtr, int, IntPtr> PlayerController;
    public static delegate* unmanaged<byte*, long> BeginCpuScope;
    public static delegate* unmanaged<long, byte> EndCpuScope;
}
