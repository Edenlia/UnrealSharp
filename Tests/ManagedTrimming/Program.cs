using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Xml.Linq;
using UnrealSharp.Automation.Utilities;

string Root = Path.GetFullPath(Path.Combine("Saved", "ManagedTrimTests", Guid.NewGuid().ToString("N")));
string Props = Path.GetFullPath("UnrealSharp.Build.props");
string Package = Path.Combine(Root, "package with spaces");
string LinkWork = Path.Combine(Root, "link work");
string Host = Path.Combine(Root, "UnrealSharp.Plugins", "UnrealSharp.Plugins.csproj");
string Game = Path.Combine(Root, "Game", "Fixture.cs");

CreateProject("UnrealSharp.Core",
    """
    namespace UnrealSharp.Attributes;
    public sealed class UClassAttribute : System.Attribute;
    public sealed class UStructAttribute : System.Attribute;
    public sealed class UEnumAttribute : System.Attribute;
    public sealed class UInterfaceAttribute : System.Attribute;
    """);
CreateProject("UnrealSharp.Binds",
    """
    namespace UnrealSharp.Binds;
    public sealed class NativeCallbacksAttribute : System.Attribute;
    [NativeCallbacks] internal static class NativeImports { internal static int Field = 42; }
    """);
CreateProject("UnrealSharp.Log", "public sealed class FixtureLog;");
CreateProject("UnrealSharp",
    """
    using UnrealSharp.Attributes;
    namespace Fixture;
    [UClass] public class NativeActor
    {
        public int Component => 42;
        public int DeadProperty => -1;
        public static int CalledByRetainedDependency() => 42;
        public static int UnusedMethod() => -1;
        private int Invoke_OnTick() => 42;
    }
    [UStruct] public struct DataRow
    {
        private readonly int _value;
        public DataRow() => _value = 42;
        public int ToNative() => _value;
    }
    [UInterface] public interface INative
    {
        public static object Wrap(object value) => value;
    }
    [UEnum] public enum Kind { Unknown, Configured = 42 }
    public delegate int Done();
    internal sealed class Done__DelegateSignature { public int Invoke() => 42; }
    public sealed class DeadBinding;
    """, ["UnrealSharp.Core"]);
CreateProject("Glue",
    """
    using UnrealSharp.Attributes;
    namespace Fixture;
    [UClass] public class NativePluginActor : NativeActor;
    """, ["UnrealSharp", "UnrealSharp.Core"]);
CreateProject("RetainedDependency",
    """
    public static class Dependency { public static int Run() => Fixture.NativeActor.CalledByRetainedDependency(); }
    """, ["UnrealSharp"]);
CreateProject("Game",
    """
    using System;
    using System.Runtime.CompilerServices;
    using UnrealSharp.Attributes;
    namespace Fixture;
    internal static class Entry
    {
        [ModuleInitializer] internal static void Start() => AppContext.SetData("UnrealSharp.TrimFixture", 42);
    }
    public class Outer
    {
        [UClass] public class ManagedActor : NativePluginActor
        {
            private ManagedActor() { }
            private int Invoke_ReceiveEvent() => 42;
            public int ConfiguredComponent => 42;
        }
    }
    internal sealed class DeadGameplay;
    """, ["Glue", "UnrealSharp.Core"], hasDescriptor: true);
File.WriteAllText(Path.Combine(Root, "Game", "Roots.xml"),
    """
    <linker>
      <assembly fullname="UnrealSharp">
        <type fullname="Fixture.NativeActor" preserve="nothing">
          <property name="Component" accessors="get" />
        </type>
      </assembly>
      <assembly fullname="Game">
        <type fullname="Fixture.Outer/ManagedActor" preserve="nothing">
          <property name="ConfiguredComponent" accessors="get" />
        </type>
      </assembly>
    </linker>
    """);
CreateProject("UnrealSharp.Plugins",
    """
    using System.Runtime.InteropServices;
    namespace UnrealSharp.Plugins;
    internal static class Main
    {
        [UnmanagedCallersOnly] private static int InitializeUnrealSharp() => 42;
    }
    """, ["Game", "UnrealSharp.Binds", "UnrealSharp.Log", "RetainedDependency"]);

Publish(Package);
CheckPackageReferences(Package);
string[] AotInputNames = ["UnrealSharp", "UnrealSharp.Binds", "UnrealSharp.Core", "UnrealSharp.Log", "UnrealSharp.Plugins", "Glue", "Game", "RetainedDependency"];
string? AotInputs = null;
if (args.Contains("--nativeaot", StringComparer.Ordinal))
{
    AotInputs = Path.Combine(Root, "NativeAotInputs");
    Directory.CreateDirectory(AotInputs);
    foreach (string Name in AotInputNames)
    {
        File.Copy(Path.Combine(Package, Name + ".dll"), Path.Combine(AotInputs, Name + ".dll"));
    }
}
string[] Untouched = ["UnrealSharp.Plugins.deps.json", "System.Private.CoreLib.dll", "RetainedDependency.dll", "hostfxr" + NativeExtension()];
string[] OriginalFiles = Untouched.Select(file => file.StartsWith("hostfxr", StringComparison.Ordinal) && !OperatingSystem.IsWindows()
    ? "lib" + file : file).ToArray();
Dictionary<string, string> Before = OriginalFiles.ToDictionary(file => file, file => Hash(Package, file));
long InitialSize = new FileInfo(Path.Combine(Package, "UnrealSharp.dll")).Length;
ManagedTrimmingUtilities.Trim(Package, LinkWork);
Require(new FileInfo(Path.Combine(Package, "UnrealSharp.dll")).Length < InitialSize, "engine bindings did not shrink");
foreach ((string File, string OriginalHash) in Before)
{
    Require(Hash(Package, File) == OriginalHash, $"{File} was modified");
}

AssemblyLoadContext Context = new("trim-test", isCollectible: true);
Context.Resolving += (loadContext, name) => loadContext.LoadFromAssemblyPath(Path.Combine(Package, name.Name + ".dll"));
Assembly Bindings = Context.LoadFromAssemblyPath(Path.Combine(Package, "UnrealSharp.dll"));
Assembly GameAssembly = Context.LoadFromAssemblyPath(Path.Combine(Package, "Game.dll"));
Assembly HostAssembly = Context.LoadFromAssemblyPath(Path.Combine(Package, "UnrealSharp.Plugins.dll"));
System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(GameAssembly.ManifestModule.ModuleHandle);
Require(Equals(AppContext.GetData("UnrealSharp.TrimFixture"), 42), "module initializer");
Type Actor = GameAssembly.GetType("Fixture.Outer+ManagedActor", throwOnError: true)!;
object Instance = Activator.CreateInstance(Actor, nonPublic: true)!;
Require(Equals(Invoke(Actor, "Invoke_ReceiveEvent", Instance), 42), "private managed event");
Require(Equals(Invoke(Actor.BaseType!.BaseType!, "Invoke_OnTick", Instance), 42), "inherited native event");
Require(Equals(Actor.GetProperty("ConfiguredComponent")!.GetValue(Instance), 42), "configured project property");
Require(Equals(Actor.GetProperty("Component")!.GetValue(Instance), 42), "configured binding property");
Require(Actor.GetProperty("DeadProperty") == null, "unused Unreal property was kept");
Require(GameAssembly.GetType("Fixture.DeadGameplay") == null && Bindings.GetType("Fixture.DeadBinding") == null, "unused type was kept");
Type Struct = Bindings.GetType("Fixture.DataRow", throwOnError: true)!;
Require(Equals(Invoke(Struct, "ToNative", Activator.CreateInstance(Struct)), 42), "struct default and marshalling");
Require(ReferenceEquals(Bindings.GetType("Fixture.INative")!.GetMethod("Wrap")!.Invoke(null, [Instance]), Instance), "interface wrap");
Require(Equals(Enum.Parse(Bindings.GetType("Fixture.Kind")!, "Configured").ToString(), "Configured"), "enum name");
Type DelegateWrapper = Bindings.GetType("Fixture.Done__DelegateSignature", throwOnError: true)!;
Require(Equals(Invoke(DelegateWrapper, "Invoke", Activator.CreateInstance(DelegateWrapper)), 42), "delegate wrapper");
Assembly Binds = Context.LoadFromAssemblyPath(Path.Combine(Package, "UnrealSharp.Binds.dll"));
Require(Equals(Binds.GetType("UnrealSharp.Binds.NativeImports")!.GetField("Field", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null), 42), "native binder field");
Assembly Dependency = Context.LoadFromAssemblyPath(Path.Combine(Package, "RetainedDependency.dll"));
Require(Equals(Dependency.GetType("Dependency")!.GetMethod("Run")!.Invoke(null, null), 42), "dependency into trimmed bindings");
unsafe
{
    var EntryPoint = (delegate* unmanaged<int>)HostAssembly.GetType("UnrealSharp.Plugins.Main")!
        .GetMethod("InitializeUnrealSharp", BindingFlags.Static | BindingFlags.NonPublic)!.MethodHandle.GetFunctionPointer();
    Require(EntryPoint() == 42, "native host entry point");
}
Context.Unload();

if (AotInputs != null)
{
    NativeAotProbe.Verify(AotInputs, AotInputNames, Path.Combine(LinkWork, "UnrealSharp.Roots.xml"),
        Path.Combine(Root, "Game", "Roots.xml"));
}

File.AppendAllText(Game,
    """

    [UClass] internal class DynamicReflection
    {
        private Type? Invoke_Dynamic() => typeof(DynamicReflection).Assembly.GetType(Environment.GetEnvironmentVariable("FixtureType")!);
    }
    """);
string BadPackage = Path.Combine(Root, "bad package");
Publish(BadPackage);
string BadAssemblyHash = Hash(BadPackage, "Game.dll");
bool Rejected = false;
try
{
    ManagedTrimmingUtilities.Trim(BadPackage, Path.Combine(Root, "bad link"));
}
catch (InvalidOperationException)
{
    Rejected = true;
}
Require(Rejected && BadAssemblyHash == Hash(BadPackage, "Game.dll"), "unresolved reflection must fail before touching the original DLL");
File.Delete(Path.Combine(BadPackage, "Glue.dll"));
try
{
    ManagedTrimmingUtilities.Trim(BadPackage, Path.Combine(Root, "missing glue link"));
    throw new Exception("Missing load-order assembly was accepted.");
}
catch (InvalidDataException Exception)
{
    Require(Exception.Message.Contains("Glue.dll", StringComparison.Ordinal), "missing glue diagnostic");
}
Console.WriteLine($"Managed trimming verification passed. Work directory {Root}");

void CreateProject(string name, string code, string[]? references = null, bool hasDescriptor = false)
{
    string DirectoryPath = Path.Combine(Root, name);
    Directory.CreateDirectory(DirectoryPath);
    File.WriteAllText(Path.Combine(DirectoryPath, "Fixture.cs"), code);
    XElement Project = new("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
        new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"),
            new XElement("GenerateRuntimeConfigurationFiles", "true"), new XElement("Nullable", "enable")),
        new XElement("Import", new XAttribute("Project", Props)),
        new XElement("ItemGroup",
            (references ?? []).Select(reference => new XElement("ProjectReference",
                new XAttribute("Include", $"../{reference}/{reference}.csproj"))),
            hasDescriptor ? new XElement("TrimmerRootDescriptor", new XAttribute("Include", "Roots.xml")) : null));
    Project.Save(Path.Combine(DirectoryPath, name + ".csproj"));
}

void Publish(string output)
{
    using Process Process = new() { StartInfo = new ProcessStartInfo("dotnet") };
    foreach (string Argument in new[] { "publish", Host, "-r", RuntimeInformation.RuntimeIdentifier, "--self-contained",
        "--output", output, "-p:UnrealSharpExportTrimDescriptors=true", "-p:UETargetType=Game" })
    {
        Process.StartInfo.ArgumentList.Add(Argument);
    }
    Process.Start();
    Process.WaitForExit();
    Require(Process.ExitCode == 0, "fixture publish");
    File.WriteAllText(Path.Combine(output, "GlueCode.LoadOrder.json"), """{"LoadOrder":["Glue"]}""");
    File.WriteAllText(Path.Combine(output, "UserCode.LoadOrder.json"), """{"LoadOrder":["Game"]}""");
    Require(Directory.EnumerateFiles(Path.Combine(output, "UnrealSharpTrimming"), "*.xml", SearchOption.AllDirectories).Count() == 1,
        "descriptor from a project reference");
}

void CheckPackageReferences(string package)
{
    string DirectoryPath = Path.Combine(Root, "ReferenceProbe");
    Directory.CreateDirectory(DirectoryPath);
    string ProjectPath = Path.Combine(DirectoryPath, "ReferenceProbe.csproj");
    new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
        new XElement("Import", new XAttribute("Project", Path.GetFullPath("UnrealSharp.Shared.props"))),
        new XElement("ItemGroup", new XElement("Analyzer", new XAttribute("Remove", "@(Analyzer)")),
            new XElement("Reference", new XAttribute("Include", "Glue"), new XElement("HintPath", "missing/Glue.dll"))))
        .Save(ProjectPath);
    File.WriteAllText(Path.Combine(DirectoryPath, "Game.cs"), "public class Game : Fixture.NativePluginActor { }");
    using Process Process = new() { StartInfo = new ProcessStartInfo("dotnet") };
    Process.StartInfo.ArgumentList.Add("build");
    Process.StartInfo.ArgumentList.Add(ProjectPath);
    Process.StartInfo.ArgumentList.Add($"-p:UnrealSharpPackageReferencePath={package}");
    Process.Start();
    Process.WaitForExit();
    Require(Process.ExitCode == 0, "manage references must resolve from the package without using framework implementation assemblies");
}

static string NativeExtension() => OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
static string Hash(string folder, string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, file))));
static object? Invoke(Type type, string name, object? instance) =>
    type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(instance, null);
static void Require(bool success, string message)
{
    if (!success) throw new Exception(message);
}
