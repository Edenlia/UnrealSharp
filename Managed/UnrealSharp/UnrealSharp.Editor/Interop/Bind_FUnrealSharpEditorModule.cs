using UnrealSharp.Binds;
using UnrealSharp.Core;

namespace UnrealSharp.Editor.Interop;

[NativeCallbacks]
public static unsafe partial class Bind_FUnrealSharpEditorModule
{
    public static delegate* unmanaged<FManagedUnrealSharpEditorCallbacks, void> InitializeUnrealSharpEditorCallbacks;
    public static delegate* unmanaged<out UnmanagedArray, void> GetProjectPaths;
    public static delegate* unmanaged<IntPtr, ECSTypeStructuralFlags, void> DirtyUnrealType;
    public static delegate* unmanaged<void> NotifyNewType;
    public static delegate* unmanaged<char*, char*, int, int, char*, int, char*, char*, int, void> ReportCompileDiagnostic;
    public static delegate* unmanaged<char*, void> ClearFileCompileDiagnostics;
    public static delegate* unmanaged<char*, void> BeginProjectCompileDiagnostics;
}