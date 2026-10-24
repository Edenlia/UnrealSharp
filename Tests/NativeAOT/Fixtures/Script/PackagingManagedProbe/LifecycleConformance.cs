using UnrealSharp.Core;
using UnrealSharp.Attributes;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine.Core.Modules;
using UnrealSharp.Plugins;

namespace PackagingManagedProbe;

[UModule]
public class ConformanceModule : IModuleInterface
{
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public string StartupName { get; private set; } = string.Empty;

    public void StartupModule()
    {
        Starts++;
        StartupName = new FName("ModuleCallbacksReady").ToString();
        Console.WriteLine("NATIVEAOT MODULE startup " + Starts);
    }

    public void ShutdownModule()
    {
        Stops++;
        Console.WriteLine("NATIVEAOT MODULE shutdown " + Stops);
    }
}

public static class LifecycleConformance
{
    public static unsafe void Run(Action<bool, string> check)
    {
        Plugin? plugin = PluginLoader.FindPlugin(typeof(ConformanceModule));
        ConformanceModule module = PluginLoader.FindModule<ConformanceModule>();
        check(plugin != null && ReferenceEquals(plugin.Assembly!.Target, typeof(ConformanceModule).Assembly)
            && ReferenceEquals(module, plugin.GetModule<ConformanceModule>()),
            "logical assembly and generated module registration preserve identity");
        check(module.Starts == 1 && module.Stops == 0 && module.StartupName == "ModuleCallbacksReady",
            "module starts exactly once after native callbacks are ready");
        check(!plugin!.Load() && module.Starts == 1, "repeated plugin load does not repeat module startup");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        check(ReferenceEquals(module, PluginLoader.FindModule<ConformanceModule>()) && module.Stops == 0,
            "module instance remains registered after managed GC");

#if NATIVE_AOT
        WeakReference assembly = plugin.Assembly!;
        PluginsCallbacks callbacks = default;
        PluginsCallbacks.Initialize(&callbacks);
        fixed (char* name = "PackagingManagedProbe.dll") callbacks.UnloadPlugin(name);
        check(ReferenceEquals(PluginLoader.FindPlugin(typeof(ConformanceModule)), plugin)
            && ReferenceEquals(plugin.Assembly, assembly) && module.Stops == 0,
            "AOT unload callback rejects without removing registry or module state");
        bool rejected = false;
        try { plugin.Unload(); }
        catch (PlatformNotSupportedException) { rejected = true; }
        check(rejected && ReferenceEquals(module, plugin.GetModule<ConformanceModule>()) && module.Stops == 0,
            "AOT direct unload rejects before module shutdown");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        check(UObject.NewObject<UFunctionBaseProbe>().NoArguments() == 47 && ReferenceEquals(plugin.Assembly!.Target, typeof(ConformanceModule).Assembly),
            "AOT callbacks and assembly handles remain usable after rejected unload and GC");
#endif
    }
}
