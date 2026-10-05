using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnrealSharp.Editor;
using UnrealSharp.GlueGenerator;

// Uses the actual Editor change detector and generator. No Editor/native runtime
// is loaded here; the separate Editor reproduction covers Blueprint rebuilding.
internal static class Program
{
    private const string Preamble = "using UnrealSharp.Attributes; namespace Repro; ";
    private const string EmptyPart = Preamble + "public partial class AProbe { }";
    private const string PropertyPart = Preamble + "public partial class AProbe { [UProperty] public partial int Value { get; set; } }";
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview);
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    private static int _passed;

    private const string Contracts = """
        namespace UnrealSharp.Attributes
        {
            public class UClassAttribute : System.Attribute { }
            public class UStructAttribute : System.Attribute { }
            public class UEnumAttribute : System.Attribute { }
            public class UInterfaceAttribute : System.Attribute { }
            public class UPropertyAttribute : System.Attribute { }
            public class UFunctionAttribute : System.Attribute { }
        }
        namespace UnrealSharp.CoreUObject { public class UObject { } }
        """;

    private static SyntaxTree Tree(string source, string path) => CSharpSyntaxTree.ParseText(source, ParseOptions, path);

    private static CSharpCompilation Compilation(params SyntaxTree[] trees) => CSharpCompilation.Create(
        "PartialRepro", new[] { Tree(Contracts, "Contracts.cs") }.Concat(trees), References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        _passed++;
    }

    private static IReadOnlyList<UnrealTypeChange> Change(string before, string after, string? main = null)
    {
        SyntaxTree mainTree = Tree(main ?? Preamble + "[UClass] public partial class AProbe : UnrealSharp.CoreUObject.UObject { }", "Main.cs");
        SyntaxTree oldTree = Tree(before, "Extra.cs");
        SyntaxTree newTree = Tree(after, "Extra.cs");
        CSharpCompilation oldCompilation = Compilation(mainTree, oldTree);
        return UnrealTypeChangeDetector.GetChanges(oldCompilation, oldTree,
            oldCompilation.ReplaceSyntaxTree(oldTree, newTree), newTree);
    }

    private static void Main()
    {
        Check(Change(EmptyPart, PropertyPart) is [{ Flags: ECSTypeStructuralFlags.StructuralChanges }],
            "adding property in unannotated partial dirties its UClass");
        Check(Change(PropertyPart, EmptyPart).Count == 1, "removing property in unannotated partial dirties its UClass");
        Check(Change(PropertyPart, PropertyPart.Replace("int Value", "double Value")).Count == 1, "changing property type dirties its UClass");
        Check(Change(EmptyPart, Preamble + "public partial class AProbe { [UFunction] public int Probe() => 42; }").Count == 1,
            "adding reflected function dirties its UClass");
        Check(Change(PropertyPart, PropertyPart).Count == 0, "unchanged save is not structurally dirty");
        Check(Change(PropertyPart, "// comment\n" + PropertyPart.Replace("int Value", "int   Value")).Count == 0,
            "whitespace and comments are not structurally dirty");
        Check(Change("namespace Other; public partial class AProbe { }", "namespace Other; public partial class AProbe { public int Value; }").Count == 0,
            "same simple name in an ordinary namespace is not an Unreal type");
        Check(Change(EmptyPart, PropertyPart, Preamble + "public partial class AProbe { }").Count == 0,
            "ordinary partial type stays outside Unreal reflection");
        Check(Change(EmptyPart, PropertyPart, "namespace Repro; [UnrealSharp.Attributes.UClassAttribute] public partial class AProbe : UnrealSharp.CoreUObject.UObject { }").Count == 1,
            "fully qualified type attribute is recognized");
        Check(Change(EmptyPart, PropertyPart, "using ClassTag = UnrealSharp.Attributes.UClassAttribute; namespace Repro; [ClassTag] public partial class AProbe : UnrealSharp.CoreUObject.UObject { }").Count == 1,
            "aliased type attribute is recognized");
        Check(Change(EmptyPart.Replace("class", "struct"), PropertyPart.Replace("class", "struct"),
            Preamble + "[UStruct] public partial struct AProbe { }").Count == 1, "unannotated UStruct part is recognized");
        Check(Change(EmptyPart.Replace("class", "interface"),
            Preamble + "public partial interface AProbe { void Probe(); }",
            Preamble + "[UInterface] public partial interface AProbe { }").Count == 1, "unannotated UInterface part is recognized");

        string ctor = Preamble + "public partial class AProbe { public AProbe() { int value = 1; } }";
        ECSTypeStructuralFlags both = ECSTypeStructuralFlags.StructuralChanges | ECSTypeStructuralFlags.ConstructorChanges;
        Check(Change(EmptyPart, ctor) is [{ Flags: var flags }] && flags == both, "adding constructor in partial sets constructor flag");
        Check(Change(ctor, ctor.Replace("= 1", "= 2")) is [{ Flags: var changedFlags }] && changedFlags == both,
            "constructor body change in partial retains constructor flag");
        Check(Change(ctor, EmptyPart) is [{ Flags: var removedFlags }] && removedFlags == both,
            "removing constructor in partial retains constructor flag");
        Check(Change(EmptyPart, PropertyPart + " public partial class AProbe { public void Helper() { } }").Count == 1,
            "multiple declarations in one file notify the type once");
        Check(Change(PropertyPart, "") is [{ Flags: ECSTypeStructuralFlags.StructuralChanges }],
            "removing last declaration from an existing file dirties surviving type");

        SyntaxTree main = Tree(Preamble + "[UClass] public partial class AProbe : UnrealSharp.CoreUObject.UObject { }", "Main.cs");
        SyntaxTree extra = Tree(PropertyPart, "Extra.cs");
        CSharpCompilation initial = Compilation(main);
        CSharpCompilation added = initial.AddSyntaxTrees(extra);
        Check(UnrealTypeChangeDetector.GetChanges(initial, null, added, extra).Count == 1, "adding a partial file dirties existing type");
        Check(UnrealTypeChangeDetector.GetChanges(added, extra, initial, null).Count == 1, "deleting a partial file dirties surviving type");

        SyntaxTree collisions = Tree("""
            namespace Left { [UnrealSharp.Attributes.UClass] public partial class AProbe { public AProbe() { int n = 1; } } }
            namespace Right { [UnrealSharp.Attributes.UClass] public partial class AProbe { public AProbe() { int n = 2; } } }
            """, "Collisions.cs");
        SyntaxTree changedCollisions = Tree(collisions.ToString().Replace("int n = 2", "int n = 3"), "Collisions.cs");
        CSharpCompilation collisionCompilation = Compilation(collisions);
        Check(UnrealTypeChangeDetector.GetChanges(collisionCompilation, collisions,
            collisionCompilation.ReplaceSyntaxTree(collisions, changedCollisions), changedCollisions).Count == 1,
            "identically named types in different namespaces compare independently");

        // Reuse one driver exactly as the Editor does. Only Extra.cs changes;
        // the annotated Main.cs SyntaxTree is deliberately retained throughout.
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new UnrealTypeDiscoveryGenerator().AsSourceGenerator())
            .WithUpdatedParseOptions(ParseOptions);
        CSharpCompilation previous = initial;
        SyntaxTree? previousTree = null;
        string[] versions = [PropertyPart, PropertyPart.Replace("Value", "SecondValue"), EmptyPart, PropertyPart];
        foreach (string version in versions)
        {
            SyntaxTree nextTree = Tree(version, "Extra.cs");
            CSharpCompilation next = previousTree == null ? previous.AddSyntaxTrees(nextTree) : previous.ReplaceSyntaxTree(previousTree, nextTree);
            Check(UnrealTypeChangeDetector.GetChanges(previous, previousTree, next, nextTree).Count == 1,
                "incremental edit sends a dirty notification");
            driver = driver.RunGeneratorsAndUpdateCompilation(next, out _, out var diagnostics);
            Check(!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "incremental generator has no errors: " + string.Join("; ", diagnostics));
            string generated = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
            string? expected = version == EmptyPart ? null : version.Contains("SecondValue") ? "SecondValue" : "Value";
            Check(expected == null ? !generated.Contains("AProbe_Value_Offset") && !generated.Contains("AProbe_SecondValue_Offset")
                    : generated.Contains("AProbe_" + expected + "_Offset") && generated.Contains("\"" + expected + "\""),
                "incremental output follows partial member addition/rename/removal");
            previous = next;
            previousTree = nextTree;
        }

        Console.WriteLine($"All {_passed} partial reflection/reload checks passed.");
    }
}
