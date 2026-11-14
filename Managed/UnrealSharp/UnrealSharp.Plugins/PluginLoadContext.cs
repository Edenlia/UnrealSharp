#if !NATIVE_AOT
using System.Reflection;
using System.Runtime.Loader;
using System.IO;

namespace UnrealSharp.Plugins;

public class PluginLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// AppContext switch set to false by runtime hosts that do not start CoreCLR through hostfxr (for example the
    /// embedded iOS host). AssemblyDependencyResolver needs hostpolicy, which those hosts do not ship; they put every
    /// assembly on the trusted platform assembly list instead, so plain name binding finds them.
    /// </summary>
    public const string UseDependencyResolverSwitch = "UnrealSharp.UseAssemblyDependencyResolver";

    private readonly AssemblyDependencyResolver? _resolver;

    public PluginLoadContext(string pluginName, AssemblyDependencyResolver? resolver, bool isCollectible) : base(pluginName, isCollectible)
    {
        _resolver = resolver;
    }

    public static AssemblyDependencyResolver? CreateDependencyResolver(string assemblyPath)
    {
        bool useResolver = !AppContext.TryGetSwitch(UseDependencyResolverSwitch, out bool isEnabled) || isEnabled;
        return useResolver ? new AssemblyDependencyResolver(assemblyPath) : null;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName.Name))
        {
            return null;
        }
        
        Assembly? loadedAssembly = AssemblyCache.GetAssembly(assemblyName.Name!, this);
        if (loadedAssembly != null)
        {
            return loadedAssembly;
        }
        
        string? assemblyPath = _resolver?.ResolveAssemblyToPath(assemblyName);
        
        Assembly? newAssembly;
        if (string.IsNullOrEmpty(assemblyPath))
        {
            newAssembly = Default.LoadFromAssemblyName(assemblyName);
        }
        else
        {
            using FileStream assemblyFile = File.Open(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        
            if (File.Exists(pdbPath))
            {
                using FileStream pdbFile = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                newAssembly = LoadFromStream(assemblyFile, pdbFile);
            }
            else
            {
                newAssembly = LoadFromAssemblyPath(assemblyPath);
            }
        }

        AssemblyCache.AddAssembly(newAssembly);
        return newAssembly;
    }
}
#endif
