using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using UnrealSharp.Automation.Utilities;

if (args is ["probe", string Payload])
{
    Probe(Payload);
    return;
}

DirectoryInfo Work = Directory.CreateTempSubdirectory("UnrealSharpTrimTests-");
string Project = Path.GetFullPath("../../../Fixture/Host/Host.csproj", AppContext.BaseDirectory);
string Good = Path.Combine(Work.FullName, "good");
string Unsafe = Path.Combine(Work.FullName, "unsafe");
Console.WriteLine($"Using test directory {Work.FullName}");

Publish(Good);
string GameAssembly = Path.Combine(Good, "Game.dll");
long Before = new FileInfo(GameAssembly).Length;
string HostHash = Hash(Path.Combine(Good, "UnrealSharp.Plugins.dll"));
string RuntimeHash = Hash(Path.Combine(Good, "System.Private.CoreLib.dll"));
string DepsHash = Hash(Path.Combine(Good, "UnrealSharp.Plugins.deps.json"));
string Manifest = Path.Combine(Good, "UserCode.LoadOrder.json");
File.WriteAllText(Manifest, """{"Priority":0,"Collectible":false,"LoadOrder":["Game"]}""");
string ManifestHash = Hash(Manifest);

ManagedTrimmingUtilities.Trim(Good, Work.FullName);
Require(new FileInfo(GameAssembly).Length <= Before, "Linked fixture grew.");
Require(HostHash == Hash(Path.Combine(Good, "UnrealSharp.Plugins.dll")), "Host assembly changed.");
Require(RuntimeHash == Hash(Path.Combine(Good, "System.Private.CoreLib.dll")), "Runtime assembly changed.");
Require(DepsHash == Hash(Path.Combine(Good, "UnrealSharp.Plugins.deps.json")), "Dependency manifest changed.");
Require(ManifestHash == Hash(Manifest), "Load order changed.");
RunDotnet([Assembly.GetExecutingAssembly().Location, "probe", Good]);

Publish(Unsafe, "-p:DefineConstants=UNSAFE_REFLECTION");
string UnsafeGame = Path.Combine(Unsafe, "Game.dll");
string UnsafeHash = Hash(UnsafeGame);
ExpectFailure(() => ManagedTrimmingUtilities.Trim(Unsafe, Work.FullName),
    exception => exception.Message.Contains("Process exited with code", StringComparison.Ordinal));
Require(UnsafeHash == Hash(UnsafeGame), "A failed link changed the published assembly.");

File.WriteAllText(Manifest, """{"LoadOrder":["MissingAssembly"]}""");
ExpectFailure(() => ManagedTrimmingUtilities.Trim(Good, Work.FullName), exception => exception is FileNotFoundException);
Work.Delete(recursive: true);
Console.WriteLine("Managed trimming smoke tests passed.");

void Publish(string Output, params string[] ExtraArguments) => RunDotnet([
    "publish", Project, "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier, "--self-contained", "true",
    "-o", Output, "-p:UseDefaultOutputPath=true", .. ExtraArguments
]);

static void Probe(string payload)
{
    AssemblyLoadContext Context = AssemblyLoadContext.Default;
    Context.Resolving += (_, name) =>
    {
        string PathName = Path.Combine(payload, name.Name + ".dll");
        return File.Exists(PathName) ? Context.LoadFromAssemblyPath(PathName) : null;
    };
    Assembly Game = Context.LoadFromAssemblyPath(Path.Combine(payload, "Game.dll"));
    Type Actor = Game.GetType("Game.Actor", throwOnError: true)!;
    object ActorInstance = Activator.CreateInstance(Actor, nonPublic: true)!;
    Require((int)Actor.GetMethod("Invoke_Started", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ActorInstance, null)! == 42,
        "Constructor, module initializer or native invocation target was lost.");
    Require((int)Actor.GetProperty("State")!.GetValue(ActorInstance)! == 40, "Reflected property was lost.");
    Require(Actor.GetMethod("Unused") == null, "Unused game method was retained.");
    Require((string)Actor.GetMethod("ReadResource")!.Invoke(null, null)! == "fixture resource", "Embedded resource failed to load.");

    Type Structure = Game.GetType("Game.Payload", throwOnError: true)!;
    Require((int)Structure.GetMethod("ToNative")!.Invoke(Activator.CreateInstance(Structure), null)! == 42, "Struct defaults were lost.");
    Require((int)Game.GetType("Game.Mode", throwOnError: true)!.GetField("Active")!.GetRawConstantValue()! == 42, "Enum member was lost.");

    Type Interface = Game.GetType("Game.IWrapper", throwOnError: true)!;
    object Wrapper = Interface.GetMethod("Wrap")!.Invoke(null, [42])!;
    Require((int)Interface.GetMethod("Value")!.Invoke(Wrapper, null)! == 42, "Native interface wrapping failed.");
    Require(Game.GetType("Game.Callbacks", throwOnError: true)!.GetField("Pointer") != null, "Callback field was lost.");
    Type Delegate = Game.GetType("Game.Callback__DelegateSignature", throwOnError: true)!;
    Require((int)Delegate.GetMethod("Invoke")!.Invoke(Activator.CreateInstance(Delegate), null)! == 42, "Delegate wrapper failed.");

    Type Reflected = Game.GetType("Game.ReflectionTarget", throwOnError: true)!;
    Require((int)Reflected.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)! == 42,
        "Explicit project reflection root was lost.");
    Require(Reflected.GetMethod("Unused") == null, "Unused method on a reflected type was retained.");

    Assembly Host = Context.LoadFromAssemblyPath(Path.Combine(payload, "UnrealSharp.Plugins.dll"));
    Require((int)Host.GetType("TestHost.Consumer", throwOnError: true)!.GetMethod("ReadGameDependency")!.Invoke(null, null)! == 42,
        "An ordinary call from a retained assembly failed.");
}

static void RunDotnet(IEnumerable<string> arguments)
{
    ProcessStartInfo Start = new(DotNetUtilities.DotNetExecutable) { UseShellExecute = false };
    foreach (string Argument in arguments)
    {
        Start.ArgumentList.Add(Argument);
    }
    using Process Child = Process.Start(Start)!;
    Child.WaitForExit();
    Require(Child.ExitCode == 0, $"dotnet exited with code {Child.ExitCode}.");
}

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

static void ExpectFailure(Action action, Func<Exception, bool> expected)
{
    try
    {
        action();
    }
    catch (Exception Exception) when (expected(Exception))
    {
        return;
    }
    throw new InvalidOperationException("Expected the trim to fail.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

namespace UnrealSharp.Automation.Utilities
{
    internal static class LoadOrderUtilities
    {
        internal const string GlueLoadOrderName = "GlueCode";
    }

    internal static class LoggerUtilities
    {
        internal static void LogUnrealSharpInfo(string message) => Console.WriteLine(message);
        internal static void LogUnrealSharpWarning(string message) => Console.Error.WriteLine(message);
        internal static void LogUnrealSharpError(string message) => Console.Error.WriteLine(message);
    }
}
