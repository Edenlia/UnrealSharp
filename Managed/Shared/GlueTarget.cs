using System.IO;

namespace UnrealSharp.Shared;

/// <summary>
/// Inputs and existing path conventions for a glue build. Constructed by the
/// UHT or Automation entry point; does not discover modules or perform I/O.
/// </summary>
public sealed class GlueTarget
{
    public string Type { get; }
    public string Configuration { get; }
    public string? ProjectDirectory { get; }
    public string? PluginDirectory { get; }

    public GlueTarget(string type, string configuration, string? projectDirectory, string? pluginDirectory = null)
    {
        Type = type;
        Configuration = configuration;
        ProjectDirectory = projectDirectory;
        PluginDirectory = pluginDirectory;
    }

    public string FrameworkProjectDirectory => Path.Combine(PluginDirectory!, "Managed", "UnrealSharp");
    public string GlueSolutionDirectory => Path.Combine(GetIntermediateDirectory(ProjectDirectory!), "Build", Type);

    // The caller retains ownership and ExtendModule redirection decisions.
    public string GetGeneratedDirectory(string root) => GetGeneratedDirectory(root, Type);
    public string GetModuleGeneratedDirectory(string root, string moduleName) =>
        Path.Combine(GetGeneratedDirectory(root), moduleName);

    public string GetProjectOutputDirectory(string targetFramework) => GetManagedOutputDirectory(ProjectDirectory!, targetFramework);

    public static string GetIntermediateDirectory(string root) => Path.Combine(root, "Intermediate", "UnrealSharp");
    public static string GetGeneratedDirectory(string root, string type) => Path.Combine(GetIntermediateDirectory(root), "UHT", type);
    public static string GetManagedOutputDirectory(string root, string targetFramework) => Path.Combine(root, "Binaries", "Managed", targetFramework);
}
