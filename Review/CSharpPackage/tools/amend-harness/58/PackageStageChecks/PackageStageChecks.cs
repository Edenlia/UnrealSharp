using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AutomationTool;
using EpicGames.Core;
using UnrealBuildTool;
using UnrealSharp.Automation.BuildCommands;

public class PackageStageChecks : BuildCommand
{
    public override void ExecuteBuild()
    {
        string ReceiptPath = ParseRequiredStringParam("Receipt");
        List<object> Results = new();
        Type Staging = typeof(PackageProject).Assembly.GetType("UnrealSharp.Automation.Utilities.UnrealSharpStaging", true)!;
        MethodInfo Stage = Staging.GetMethod("Stage", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo Uses = Staging.GetMethod("UsesUnrealSharp", BindingFlags.Static | BindingFlags.NonPublic)!;
        ProjectParams Parameters = (ProjectParams)RuntimeHelpers.GetUninitializedObject(typeof(ProjectParams));
        TargetReceipt ReadReceipt() => TargetReceipt.Read(new FileReference(ReceiptPath));
        DeploymentContext Context(params TargetReceipt[] Receipts)
        {
            DeploymentContext Result = (DeploymentContext)RuntimeHelpers.GetUninitializedObject(typeof(DeploymentContext));
            Result.StageTargets = new();
            foreach (TargetReceipt Receipt in Receipts) Result.StageTargets.Add(new StageTarget { Receipt = Receipt });
            Result.RawProjectPath = new FileReference(ParseRequiredStringParam("Project"));
            return Result;
        }
        bool Active(TargetReceipt Receipt) => (bool)Uses.Invoke(null, new object[] { Receipt })!;
        void Check(string Name, bool Passed)
        {
            Results.Add(new { name = Name, passed = Passed });
            if (!Passed) throw new AutomationException($"Boundary check failed: {Name}");
        }
        void Invoke(DeploymentContext Context) => Stage.Invoke(null, new object[] { Parameters, Context });
        void Reject(string Name, DeploymentContext Context, string Message)
        {
            try { Invoke(Context); }
            catch (TargetInvocationException Error) when (Error.InnerException is AutomationException Inner)
            { Check(Name, Inner.Message.Contains(Message, StringComparison.Ordinal)); return; }
            throw new AutomationException($"Boundary check unexpectedly published: {Name}");
        }
        Check("real-game-receipt-active", Active(ReadReceipt()));
        Invoke(Context()); Check("empty-context-noop", true);
        TargetReceipt Editor = ReadReceipt(); Editor.TargetType = TargetType.Editor;
        Check("editor-inactive", !Active(Editor));
        Invoke(Context(Editor)); Check("editor-context-noop", true);
        TargetReceipt Disabled = ReadReceipt(); Disabled.RuntimeDependencies = new RuntimeDependencyList();
        Check("plugin-absent-inactive", !Active(Disabled));
        Invoke(Context(Disabled)); Check("plugin-absent-context-noop", true);
        TargetReceipt Client = ReadReceipt(); Client.TargetType = TargetType.Client;
        Check("client-active", Active(Client));
        TargetReceipt Server = ReadReceipt(); Server.TargetType = TargetType.Server;
        Check("server-active", Active(Server));
        Reject("multiple-targets-rejected", Context(ReadReceipt(), Client), "single game");
        Reject("mixed-plugin-targets-rejected", Context(ReadReceipt(), Disabled), "single game");
        TargetReceipt Shipping = ReadReceipt(); Shipping.Configuration = UnrealTargetConfiguration.Shipping;
        Reject("multiple-configurations-rejected", Context(ReadReceipt(), Shipping), "single game");
        TargetReceipt Universal = ReadReceipt(); Universal.Platform = UnrealTargetPlatform.Mac;
        Universal.Architectures = new UnrealArchitectures(new[] { UnrealArch.X64, UnrealArch.Arm64 });
        Reject("universal-mac-rejected", Context(Universal), "single architecture");
        TargetReceipt Mobile = ReadReceipt(); Mobile.Platform = UnrealTargetPlatform.Android;
        Reject("android-rejected", Context(Mobile), "does not support platform");
        Mobile.Platform = UnrealTargetPlatform.IOS;
        Reject("ios-rejected", Context(Mobile), "does not support platform");
        TargetReceipt Foreign = ReadReceipt(); Foreign.ProjectFile = new FileReference(Path.Combine(Path.GetDirectoryName(ReceiptPath)!, "Other.uproject"));
        Reject("foreign-project-rejected", Context(Foreign), "different project");
        TargetReceipt Mismatched = ReadReceipt();
        Mismatched.RuntimeDependencies = new RuntimeDependencyList();
        Mismatched.RuntimeDependencies.Add(new RuntimeDependency(new FileReference(Path.Combine(Path.GetDirectoryName(ReceiptPath)!, "OtherPlugin", "UnrealSharp.uplugin"))));
        Reject("plugin-identity-mismatch-rejected", Context(Mismatched), "does not match the native target receipt");
        Parameters.DLCFile = new FileReference(Path.Combine(Path.GetDirectoryName(ReceiptPath)!, "TestDLC.uplugin"));
        Reject("dlc-rejected", Context(ReadReceipt()), "does not support DLC");
        bool MissingRejected = false;
        try { UnrealSharp.Automation.Utilities.CommandUtilities.RunCommand("NoSuchPackagingCommand", ParseRequiredStringParam("Project")); }
        catch (Exception Error) { MissingRejected = Error.Message.Contains("Failed to find and execute UnrealSharp automation action"); }
        Check("missing-command-rejected", MissingRejected);
        File.WriteAllText(ParseRequiredStringParam("Results"), JsonSerializer.Serialize(new
        {
            assembly = typeof(PackageProject).Assembly.Location,
            receipt = ReceiptPath,
            cases = Results
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
