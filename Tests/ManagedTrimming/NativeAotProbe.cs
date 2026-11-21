using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

internal static class NativeAotProbe
{
    internal static void Verify(string inputDirectory, string[] assemblies, params string[] descriptors)
    {
        XElement Project = XElement.Parse(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <PublishAot>true</PublishAot>
                <IlcTreatWarningsAsErrors>true</IlcTreatWarningsAsErrors>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        Project.Add(new XElement("ItemGroup",
            assemblies.Select(name => new XElement("Reference", new XAttribute("Include", name),
                new XElement("HintPath", name + ".dll"))),
            descriptors.Select(path => new XElement("TrimmerRootDescriptor", new XAttribute("Include", path)))));
        string ProjectPath = Path.Combine(inputDirectory, "NativeAotProbe.csproj");
        Project.Save(ProjectPath);
        File.WriteAllText(Path.Combine(inputDirectory, "Program.cs"),
            """
            using System.Diagnostics.CodeAnalysis;
            using System.Reflection;

            Check(typeof(Fixture.NativeActor).Assembly, typeof(Fixture.Outer).Assembly);

            [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Verify the separately generated descriptor.")]
            [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Verify the separately generated descriptor.")]
            [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Verify the separately generated descriptor.")]
            static void Check(Assembly bindings, Assembly game)
            {
                Type actor = game.GetType("Fixture.Outer+ManagedActor", throwOnError: true)!;
                object instance = Activator.CreateInstance(actor, nonPublic: true)!;
                if ((int)actor.GetProperty("ConfiguredComponent")!.GetValue(instance)! != 42 ||
                    (int)actor.GetProperty("Component")!.GetValue(instance)! != 42 ||
                    (int)actor.GetMethod("Invoke_ReceiveEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, null)! != 42 ||
                    game.GetType("Fixture.DeadGameplay") != null ||
                    bindings.GetType("Fixture.DeadBinding") != null)
                {
                    throw new Exception("NativeAOT descriptor verification failed.");
                }
                Console.WriteLine("NativeAOT generated roots and project descriptor verified.");
            }
            """);

        string Output = Path.Combine(inputDirectory, "Published");
        Run("dotnet", ["publish", ProjectPath, "-r", RuntimeInformation.RuntimeIdentifier, "-o", Output]);
        Run(Path.Combine(Output, OperatingSystem.IsWindows() ? "NativeAotProbe.exe" : "NativeAotProbe"), []);
    }

    private static void Run(string executable, IEnumerable<string> arguments)
    {
        ProcessStartInfo StartInfo = new(executable) { UseShellExecute = false };
        foreach (string Argument in arguments)
        {
            StartInfo.ArgumentList.Add(Argument);
        }

        using Process Process = Process.Start(StartInfo)!;
        Process.WaitForExit();
        if (Process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{executable}' exited with code {Process.ExitCode}.");
        }
    }
}
