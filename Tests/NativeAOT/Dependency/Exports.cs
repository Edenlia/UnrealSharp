using System.Runtime.InteropServices;

public static class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "NativeAotDependencyAnswer")]
    public static int Answer() => 42;
}
