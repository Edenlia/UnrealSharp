using System;
using System.Linq;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;

namespace UnrealSharp.Automation.Utilities;

internal static class WasmPrelink
{
    internal const string MissingProductMessage = "UnrealSharp Wasm prelink is unavailable: a matching CoreCLR/browser-host native product and production preparation backend have not been integrated. Integrate those products before building or restaging UnrealSharp for Wasm.";

    internal static bool IsWasm(UnrealTargetPlatform platform) =>
        platform.ToString().Equals("Wasm", StringComparison.OrdinalIgnoreCase);

    internal static bool ContainsWasm(ProjectParams parameters) =>
        parameters.ClientTargetPlatforms.Concat(parameters.ServerTargetPlatforms).Any(platform => IsWasm(platform.Type));

    internal static bool RequestsWasm(BuildCommand command)
    {
        string[] aliases = ["TargetPlatform", "Platform", "ServerTargetPlatform", "ServerPlatform"];
        foreach (string alias in aliases)
        {
            string[] supplied = command.Params.Where(argument => argument.StartsWith(alias + "=", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (supplied.Length > 1 && supplied.Any(argument => HasWasmName(argument[(alias.Length + 1)..].Trim('"'))))
            {
                throw new AutomationException("UnrealSharp Wasm prelink requires an unambiguous -{0}= value; duplicate platform arguments are unsupported.", alias);
            }
        }

        // ProjectParams merges both aliases. It expands '+' and dependent '.'
        // platforms, and uses client shorthand only when both aliases are empty.
        string? targetPlatform = command.ParseParamValue("TargetPlatform");
        string? platform = command.ParseParamValue("Platform");
        return HasWasmName(targetPlatform) || HasWasmName(platform) ||
            (string.IsNullOrEmpty(targetPlatform) && string.IsNullOrEmpty(platform) && command.ParseParam("Wasm")) ||
            HasWasmName(command.ParseParamValue("ServerTargetPlatform")) || HasWasmName(command.ParseParamValue("ServerPlatform"));
    }

    private static bool HasWasmName(string? value) => value != null &&
        value.Split(['+', '.'], StringSplitOptions.RemoveEmptyEntries).Any(name => name.Equals("Wasm", StringComparison.OrdinalIgnoreCase));

    internal static void RejectUnavailableExplicitRequest(BuildCommand command)
    {
        if (RequestsWasm(command))
        {
            throw new AutomationException(MissingProductMessage);
        }
    }

    internal static void Prepare(BuildCommand owner, ProjectParams parameters)
    {
        WasmGameBuildRequest request = WasmGameBuildRequest.Freeze(parameters);
        IWasmPrelinkBackend backend = ResolveProducts(request);
        var builder = new UnrealBuild(owner);
        UnrealBuild.BuildAgenda agenda = request.CreateAgenda(builder, parameters);
        builder.Build(agenda, InDeleteBuildProducts: request.Clean, InUpdateVersionFiles: false,
            InTargetToManifest: null, InSkipBuild: true);
        backend.VerifyUhtProducer(request);
        backend.BuildManagedClosureAndHelpers(request);
        backend.ValidateNativeInputs(request);
        request.RequireUnchanged(parameters);
    }

    private static IWasmPrelinkBackend ResolveProducts(WasmGameBuildRequest request)
    {
        // No descriptor/Ready schema, environment override or CLI bypass exists.
        // This must be replaced by a real product consumer before the command
        // can reach codegen, managed builds, helpers or the normal native build.
        throw new AutomationException(MissingProductMessage);
    }
}

// Behavior boundaries only. A real backend must reuse the producer protocol's
// read-only validation before building managed/native inputs. There is currently
// no executable implementation or externally selectable backend.
internal interface IWasmPrelinkBackend
{
    void VerifyUhtProducer(WasmGameBuildRequest request);
    void BuildManagedClosureAndHelpers(WasmGameBuildRequest request);
    void ValidateNativeInputs(WasmGameBuildRequest request);
}

internal sealed record WasmGameBuildRequest(FileReference ProjectFile, string TargetName,
    UnrealTargetPlatform Platform, UnrealTargetConfiguration Configuration, string UbtArguments,
    string? RequestedArchitectures, bool? Clean)
{
    internal static WasmGameBuildRequest Freeze(ProjectParams parameters)
    {
        if (!parameters.IsCodeBasedProject || parameters.CodeBasedUprojectPath == null ||
            parameters.RawProgramProjectOverride != null || parameters.IsProgramTarget || parameters.HasProgramTargets ||
            parameters.Client || parameters.NoClient || parameters.DedicatedServer || parameters.HasServerCookedTargets || parameters.HasDLCName)
        {
            throw new AutomationException("UnrealSharp Wasm prelink requires one code-based Game target, without client/server/program/DLC requests.");
        }
        if (!parameters.Build || parameters.SkipBuildClient || parameters.Clean == true)
        {
            throw new AutomationException("UnrealSharp Wasm prelink requires -build without clean or skipped client builds. Restaging needs a real prepared product and native receipt.");
        }
        if (parameters.ClientCookedTargets.Count != 1 || string.IsNullOrWhiteSpace(parameters.ClientCookedTargets[0]) ||
            parameters.ClientConfigsToBuild.Count != 1 || parameters.ClientConfigsToBuild[0] == UnrealTargetConfiguration.Unknown ||
            parameters.ClientTargetPlatforms.Count != 1 || !WasmPrelink.IsWasm(parameters.ClientTargetPlatforms[0].Type) ||
            !string.IsNullOrEmpty(parameters.ClientTargetPlatforms[0].CookFlavor) ||
            parameters.ClientDependentPlatformMap.Count != 0 || parameters.ServerDependentPlatformMap.Count != 0 ||
            parameters.ServerTargetPlatforms.Any(platform => platform.Type != parameters.ClientTargetPlatforms[0].Type || !string.IsNullOrEmpty(platform.CookFlavor)))
        {
            throw new AutomationException("UnrealSharp Wasm prelink requires one Wasm platform, Game target and configuration, without cook flavors or dependent/mixed platforms.");
        }

        // Match UE 5.9 Project.Build's Game composition, including its spacing
        // and outer-quote handling. An omitted architecture stays omitted.
        string clientArgs = parameters.AdditionalBuildOptions +
            (parameters.ClientArchitecture == null ? "" : $"-architecture={parameters.ClientArchitecture}");
        string additionalArgs = "";
        if (!string.IsNullOrEmpty(parameters.UbtArgs))
        {
            additionalArgs += " " + parameters.UbtArgs.TrimStart('"').TrimEnd('"');
        }
        if (parameters.MapFile) additionalArgs += " -mapfile";
        if (parameters.Deploy || parameters.Package) additionalArgs += " -skipdeploy";
        if (parameters.Distribution) additionalArgs += " -distribution";
        foreach (string configOverride in parameters.ConfigOverrideParams)
        {
            additionalArgs += " -" + configOverride;
        }
        string arguments = $" -remoteini=\"{parameters.RawProjectPath.Directory}\" {additionalArgs} {clientArgs}";
        return new WasmGameBuildRequest(parameters.CodeBasedUprojectPath, parameters.ClientCookedTargets[0],
            parameters.ClientTargetPlatforms[0].Type, parameters.ClientConfigsToBuild[0], arguments,
            parameters.ClientArchitecture?.ToString(), parameters.Clean);
    }

    internal UnrealBuild.BuildAgenda CreateAgenda(UnrealBuild builder, ProjectParams parameters)
    {
        RequireUnchanged(parameters);
        var agenda = new UnrealBuild.BuildAgenda();
        agenda.AddTarget(TargetName, Platform, Configuration, ProjectFile, UbtArguments);
        foreach (UnrealTargetPlatform platform in parameters.ClientTargetPlatforms.Concat(parameters.ServerTargetPlatforms).Select(platform => platform.Type).Distinct())
        {
            AutomationTool.Platform.GetPlatform(platform).PreBuildAgenda(builder, agenda, parameters);
        }
        RequireUnchanged(parameters);
        if (agenda.Targets.Count != 1 || agenda.DotNetProjects.Count != 0 || agenda.SwarmAgentProject != "" || agenda.SwarmCoordinatorProject != "")
        {
            throw new AutomationException("The platform pre-build hook changed UnrealSharp's single Game prelink agenda.");
        }
        UnrealBuild.BuildTarget target = agenda.Targets[0];
        if (target.TargetName != TargetName || target.Platform != Platform || target.Config != Configuration ||
            target.UprojectPath != ProjectFile || target.UBTArgs != UbtArguments || target.Clean != null)
        {
            throw new AutomationException("The platform pre-build hook changed the frozen UnrealSharp Game prelink request.");
        }
        return agenda;
    }

    internal void RequireUnchanged(ProjectParams parameters)
    {
        if (this != Freeze(parameters))
        {
            throw new AutomationException("UnrealSharp's Game prelink request changed after it was frozen.");
        }
    }
}
