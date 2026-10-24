using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AutomationTool;

namespace UnrealSharp.Automation.Staging;

// UAT discovers concrete handlers only in registered root automation assemblies.
public sealed class UnrealSharpProjectStagingHandler : CustomStagingHandler
{
    private CustomStagingHandler? Handler;

    protected override bool TryInitialize(ProjectParams Params, DeploymentContext SC)
    {
        const string HandlerName = "UnrealSharp.Automation.Staging.UnrealSharpStagingHandler";
        // Installed engines or an explicit ScriptDir may already register the plugin module.
        if (ScriptManager.AllScriptAssemblies.Any(assembly => assembly.GetType(HandlerName) != null))
        {
            return false;
        }

        string RelativeAssemblyPath = GetType().Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "UnrealSharpAutomationAssembly").Value!;
        string AssemblyPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location)!, RelativeAssemblyPath));
        // Load the selected plugin's module by path. Loading by name can bind an older
        // EngineLevel UnrealSharp.Automation instead of the ProjectLevel override.
        Assembly AutomationAssembly = Assembly.LoadFile(AssemblyPath);
        Type HandlerType = AutomationAssembly.GetType(HandlerName, throwOnError: true)!;
        Handler = (CustomStagingHandler)Activator.CreateInstance(HandlerType)!;
        Func<ProjectParams, DeploymentContext, bool> Initialize = HandlerType
            .GetMethod("TryInitialize", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<ProjectParams, DeploymentContext, bool>>(Handler);
        return Initialize(Params, SC);
    }

    public override void GetFilesToStage(ProjectParams Params, DeploymentContext SC)
    {
        Handler!.GetFilesToStage(Params, SC);
    }
}
