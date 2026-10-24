using System.Runtime.InteropServices;
using UnrealSharp.Attributes;
using UnrealSharp.Core;
using UnrealSharp.Core.Marshallers;
using UnrealSharp.CoreUObject;
using UnrealSharp.Interop;

namespace PackagingManagedProbe;

[UClass]
public partial class UExceptionProbe : UObject
{
    [UFunction] public void ThrowForTest() => throw new InvalidOperationException("Expected callback failure: 中文 é");
    [UFunction] public int SucceedAfterFailure() => 42;
}

public static class ExceptionConformance
{
    public static unsafe void Run(Action<bool, string> check)
    {
        // Use the installed native callback table to test the unmanaged boundary
        // without invoking UE's intentional crash-on-Blueprint-exception policy.
        ManagedCallbacks* callbacks = (ManagedCallbacks*)Bind_FCSManagedCallbacks.CallGetManagedCallbacks();
        GCHandle type = GCHandle.Alloc(typeof(UExceptionProbe));
        GCHandle instance = GCHandle.Alloc(UObject.NewObject<UExceptionProbe>());
        IntPtr throwing = IntPtr.Zero;
        IntPtr succeeding = IntPtr.Zero;
        UnmanagedArray error = default;
        try
        {
            fixed (char* name = "Invoke_ThrowForTest") throwing = callbacks->GetManagedMethod(GCHandle.ToIntPtr(type), name);
            fixed (char* name = "Invoke_SucceedAfterFailure") succeeding = callbacks->GetManagedMethod(GCHandle.ToIntPtr(type), name);
            if (throwing == IntPtr.Zero || succeeding == IntPtr.Zero) throw new InvalidOperationException("Generated exception probe callback missing");
            int status = callbacks->InvokeManagedMethod(GCHandle.ToIntPtr(instance), throwing, IntPtr.Zero, IntPtr.Zero, (IntPtr)(&error));
            string message = StringMarshaller.FromNative((IntPtr)(&error), 0);
            check(status == 1 && message.Contains("InvalidOperationException") && message.Contains("Expected callback failure: 中文 é"),
                "unmanaged callback catches generated exception and preserves Unicode error text");
            int value = 0;
            status = callbacks->InvokeManagedMethod(GCHandle.ToIntPtr(instance), succeeding, IntPtr.Zero, (IntPtr)(&value), (IntPtr)(&error));
            check(status == 0 && value == 42, "unmanaged callback succeeds after a caught managed exception");
        }
        finally
        {
            StringMarshaller.DestructInstance((IntPtr)(&error), 0);
            if (throwing != IntPtr.Zero) GCHandleUtilities.Free(GCHandle.FromIntPtr(throwing), typeof(UExceptionProbe).Assembly);
            if (succeeding != IntPtr.Zero) GCHandleUtilities.Free(GCHandle.FromIntPtr(succeeding), typeof(UExceptionProbe).Assembly);
            instance.Free();
            type.Free();
        }
    }
}
