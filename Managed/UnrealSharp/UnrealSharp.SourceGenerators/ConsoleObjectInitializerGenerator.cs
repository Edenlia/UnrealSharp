using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UnrealSharp.SourceGenerators;

/// <summary>
/// C# static fields are initialized when their type is first used, while C++ console variables register at module load.
/// For static TAutoConsoleVariable / FAutoConsoleCommand fields in static classes, this emits a module initializer
/// that queues the declaring class's static constructor for plugin loading, after native bindings and types are ready.
/// </summary>
[Generator]
public class ConsoleObjectInitializerGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor NotRegisteredOnLoad = new(
        "US0017",
        "Console object is registered on first use",
        "Console object '{0}' is declared in '{1}', which {2}. It is registered when '{1}' is first used, not when the assembly loads. Declare it in a non-generic static class that the assembly can access to register it on load.",
        "UnrealSharp",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedVariableType = new(
        "US0016",
        "Unsupported console variable type",
        "Console variable '{0}' has type '{1}'. TAutoConsoleVariable supports bool, int, float and string.",
        "UnrealSharp",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private readonly record struct ConsoleObjectField
    {
        public readonly string FieldName;
        public readonly string DeclaringTypeName;
        public readonly string? EagerTypeName;
        public readonly string? NotEagerReason;
        public readonly string? UnsupportedTypeName;
        public readonly Location? Location;

        public ConsoleObjectField(string fieldName, string declaringTypeName, string? eagerTypeName, string? notEagerReason, string? unsupportedTypeName, Location? location)
        {
            FieldName = fieldName;
            DeclaringTypeName = declaringTypeName;
            EagerTypeName = eagerTypeName;
            NotEagerReason = notEagerReason;
            UnsupportedTypeName = unsupportedTypeName;
            Location = location;
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ConsoleObjectField> fields = context.SyntaxProvider
            .CreateSyntaxProvider(IsStaticField, GetConsoleObjectFields)
            .SelectMany((fieldsInDeclaration, _) => fieldsInDeclaration);

        context.RegisterSourceOutput(fields.Collect(), GenerateSource);
    }

    private static bool IsStaticField(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is FieldDeclarationSyntax field && field.Modifiers.Any(modifier => modifier.ValueText == "static");
    }

    private static ImmutableArray<ConsoleObjectField> GetConsoleObjectFields(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        FieldDeclarationSyntax fieldDeclaration = (FieldDeclarationSyntax) context.Node;
        ImmutableArray<ConsoleObjectField>.Builder results = ImmutableArray.CreateBuilder<ConsoleObjectField>();

        foreach (VariableDeclaratorSyntax variable in fieldDeclaration.Declaration.Variables)
        {
            if (context.SemanticModel.GetDeclaredSymbol(variable, cancellationToken) is not IFieldSymbol { IsStatic: true } field
                || field.Type is not INamedTypeSymbol fieldType)
            {
                continue;
            }

            string fieldTypeName = fieldType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            bool isVariable = fieldTypeName == "global::UnrealSharp.TAutoConsoleVariable<T>";
            bool isCommand = fieldTypeName == "global::UnrealSharp.FAutoConsoleCommand";

            if (!isVariable && !isCommand)
            {
                continue;
            }

            string? unsupportedTypeName = null;
            if (isVariable)
            {
                ITypeSymbol valueType = fieldType.TypeArguments[0];
                bool isSupported = valueType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32
                    or SpecialType.System_Single or SpecialType.System_String;

                if (!isSupported)
                {
                    unsupportedTypeName = valueType.ToDisplayString();
                }
            }

            INamedTypeSymbol declaringType = field.ContainingType;
            string? notEagerReason = GetNotEagerReason(declaringType);
            string? eagerTypeName = notEagerReason == null ? declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null;

            results.Add(new ConsoleObjectField(field.Name, declaringType.ToDisplayString(), eagerTypeName, notEagerReason, unsupportedTypeName,
                variable.GetLocation()));
        }

        return results.ToImmutable();
    }

    private static string? GetNotEagerReason(INamedTypeSymbol declaringType)
    {
        if (!declaringType.IsStatic)
        {
            // Running the static constructor of an Unreal type on load would resolve native types before they are registered.
            return "isn't a static class";
        }

        for (INamedTypeSymbol? type = declaringType; type != null; type = type.ContainingType)
        {
            if (type.IsGenericType)
            {
                return "is generic";
            }

            if (type.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                return "isn't accessible from the rest of the assembly";
            }
        }

        return null;
    }

    private static void GenerateSource(SourceProductionContext context, ImmutableArray<ConsoleObjectField> fields)
    {
        foreach (ConsoleObjectField field in fields)
        {
            if (field.UnsupportedTypeName != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedVariableType, field.Location, field.FieldName, field.UnsupportedTypeName));
            }

            if (field.NotEagerReason != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(NotRegisteredOnLoad, field.Location, field.FieldName, field.DeclaringTypeName, field.NotEagerReason));
            }
        }

        string[] eagerTypeNames = fields
            .Where(field => field.EagerTypeName != null)
            .Select(field => field.EagerTypeName!)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();

        if (eagerTypeNames.Length == 0)
        {
            return;
        }

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("internal static class __UnrealSharpConsoleObjectsInitializer");
        builder.AppendLine("{");
        builder.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        builder.AppendLine("    internal static void Initialize()");
        builder.AppendLine("        => global::UnrealSharp.ModuleInitializerJobManager.AddJob(typeof(__UnrealSharpConsoleObjectsInitializer),");
        builder.AppendLine("            global::UnrealSharp.ModuleInitializerJobType.ConsoleObjectRegistration, RegisterConsoleObjects);");
        builder.AppendLine();
        builder.AppendLine("    private static void RegisterConsoleObjects()");
        builder.AppendLine("    {");

        foreach (string typeName in eagerTypeNames)
        {
            builder.AppendLine($"        global::UnrealSharp.ConsoleObjectInitializer.RegisterConsoleObjects(typeof({typeName}));");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");

        context.AddSource("ConsoleObjectsInitializer.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
    }
}
