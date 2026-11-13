using System;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace UnrealSharp.SourceGenerators;

[Generator]
public class CustomLogSourceGenerator : IIncrementalGenerator
{
    readonly record struct ClassLogInfo
    {
        public readonly string Name;
        public readonly string Namespace;
        public readonly string LogVerbosity;
        public readonly Accessibility Accessibility;
        
        public ClassLogInfo(ISymbol classSymbol, string logVerbosity)
        {
            Name = classSymbol.Name;
            Namespace = classSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : classSymbol.ContainingNamespace.ToDisplayString();
            LogVerbosity = logVerbosity;
            Accessibility = classSymbol.DeclaredAccessibility;
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ClassLogInfo> discoveryResults = context.SyntaxProvider.ForAttributeWithMetadataName("UnrealSharp.Log.CustomLog", Predicate, Transform);
        context.RegisterSourceOutput(discoveryResults, GenerateSource);
    }

    private void GenerateSource(SourceProductionContext sourceProductionContext, ClassLogInfo classLogInfo)
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("using UnrealSharp.Log;");

        if (!string.IsNullOrEmpty(classLogInfo.Namespace))
        {
            builder.AppendLine($"namespace {classLogInfo.Namespace};");
        }
        
        string accessibility = classLogInfo.Accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            _ => throw new InvalidOperationException($"Unsupported accessibility: {classLogInfo.Accessibility}")
        };

        builder.AppendLine($"{accessibility} partial class {classLogInfo.Name}");
        builder.AppendLine("{");
        AppendLogMethod(builder, classLogInfo, accessibility, "Log", $"(ELogVerbosity){classLogInfo.LogVerbosity}");
        AppendLogMethod(builder, classLogInfo, accessibility, "LogWarning", "ELogVerbosity.Warning");
        AppendLogMethod(builder, classLogInfo, accessibility, "LogError", "ELogVerbosity.Error");
        AppendLogMethod(builder, classLogInfo, accessibility, "LogFatal", "ELogVerbosity.Fatal");
        // An unimplemented partial hook and its calls are removed by the compiler.
        builder.AppendLine("    static partial void OnMessageLogged(string message, ELogVerbosity verbosity);");
        builder.AppendLine("}");

        sourceProductionContext.AddSource($"{classLogInfo.Name}_CustomLog.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    private static void AppendLogMethod(StringBuilder builder, ClassLogInfo classLogInfo, string accessibility, string method, string verbosity)
    {
        builder.AppendLine($"    {accessibility} static void {method}(string message)");
        builder.AppendLine("    {");
        builder.AppendLine($"        UnrealLogger.Log(\"{classLogInfo.Name}\", message, {verbosity});");
        builder.AppendLine($"        OnMessageLogged(message, {verbosity});");
        builder.AppendLine("    }");
    }

    private ClassLogInfo Transform(GeneratorAttributeSyntaxContext arg1, CancellationToken arg2)
    {
        const string logVerbosityDisplay = "ELogVerbosity.Display";
        AttributeData attribute = arg1.Attributes.First();
        TypedConstant firstArgument = attribute.ConstructorArguments.FirstOrDefault();
        string logVerbosity = firstArgument.IsNull ? logVerbosityDisplay : firstArgument.Value?.ToString() ?? logVerbosityDisplay;
        return new ClassLogInfo(arg1.TargetSymbol, logVerbosity);
    }

    private static bool Predicate(SyntaxNode arg1, CancellationToken arg2) => true;
}
