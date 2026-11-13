using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using UnrealSharp.SourceGenerator.Utilities;

namespace UnrealSharp.SourceGenerators;

public struct ParameterInfo
{
    public DelegateParameterInfo Parameter { get; set; }
}

[Generator]
public class NativeCallbacksWrapperGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
                static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0,
                static (syntaxContext, _) => GetClassInfoOrNull(syntaxContext));

        var classAndCompilation = classDeclarations.Combine(context.CompilationProvider);

        context.RegisterSourceOutput(classAndCompilation, (spc, pair) =>
        {
            var maybeClassInfo = pair.Left; // ClassInfo?
            var compilation = pair.Right;
            if (!maybeClassInfo.HasValue)
            {
                return;
            }
            GenerateForClass(spc, compilation, maybeClassInfo.Value);
        });
    }

    private static void GenerateForClass(SourceProductionContext context, Compilation compilation, ClassInfo classInfo)
    {
        var model = compilation.GetSemanticModel(classInfo.ClassDeclaration.SyntaxTree);
        var sourceBuilder = new StringBuilder();

        HashSet<string> namespaces = [];
        foreach (DelegateInfo delegateInfo in classInfo.Delegates)
        {
            foreach (var parameter in delegateInfo.ParametersAndReturnValue)
            {
                var typeInfo = model.GetTypeInfo(parameter.Type);
                var typeSymbol = typeInfo.Type;

                if (typeSymbol == null || typeSymbol.ContainingNamespace == null)
                {
                    continue;
                }

                if (typeSymbol is INamedTypeSymbol nts && nts.IsGenericType)
                {
                    namespaces.UnionWith(nts.TypeArguments.Where(t => t.ContainingNamespace != null).Select(t => t.ContainingNamespace!.ToDisplayString()));
                }

                namespaces.Add(typeSymbol.ContainingNamespace.ToDisplayString());
            }
        }

        if (classInfo.NullableAwareable)
        {
            sourceBuilder.AppendLine("#nullable enable");
        }
        else
        {
            sourceBuilder.AppendLine("#nullable disable");
        }
        
        sourceBuilder.AppendLine();

        foreach (string? ns in namespaces)
        {
            if (string.IsNullOrWhiteSpace(ns)) continue;
            sourceBuilder.AppendLine($"using {ns};");
        }

        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine($"namespace {classInfo.Namespace}");
        sourceBuilder.AppendLine("{");
        sourceBuilder.AppendLine($"    public static unsafe partial class {classInfo.Name}");
        sourceBuilder.AppendLine("    {");

        sourceBuilder.AppendLine("        static " + classInfo.Name + "()");
        sourceBuilder.AppendLine("        {");

        foreach (DelegateInfo delegateInfo in classInfo.Delegates)
        {
            string delegateName = delegateInfo.Name;

            string totalSizeDelegateName = delegateName + "TotalSize";
            if (!delegateInfo.HasReturnValue && delegateInfo.Parameters.Count == 0)
            {
                sourceBuilder.AppendLine($"             int {totalSizeDelegateName} = 0;");
            }
            else
            {
                sourceBuilder.Append($"             int {totalSizeDelegateName} = ");

                void AppendSizeOf(DelegateParameterInfo param)
                {
                    string typeFullName = param.Type.GetAnnotatedTypeName(model) ?? param.Type.ToString();

                    ITypeSymbol? parameterType = model.GetTypeInfo(param.Type).Type;
                    if (param.IsOutParameter || param.IsRefParameter ||
                        parameterType?.SpecialType == SpecialType.System_String || parameterType?.TypeKind == TypeKind.Delegate)
                    {
                        sourceBuilder.Append($"IntPtr.Size");
                    }
                    else
                    {
                        sourceBuilder.Append($"sizeof({typeFullName})");
                    }
                }

                List<DelegateParameterInfo> parameters = delegateInfo.ParametersAndReturnValue;

                for (int i = 0; i < parameters.Count; i++)
                {
                    AppendSizeOf(parameters[i]);

                    if (i != parameters.Count - 1)
                    {
                        sourceBuilder.Append(" + ");
                    }
                }

                sourceBuilder.AppendLine(";");
            }

            string funcPtrName = delegateName + "FuncPtr";
            sourceBuilder.AppendLine($"             IntPtr {funcPtrName} = UnrealSharp.Binds.NativeBinds.TryGetBoundFunction(\"{classInfo.Name}\", \"{delegateInfo.Name}\", {totalSizeDelegateName});");
            sourceBuilder.Append($"             {delegateName} = (delegate* unmanaged<");
            sourceBuilder.Append(string.Join(", ", delegateInfo.Parameters.Select(p =>
            {
                string prefix = p.IsOutParameter ? "out " : p.IsRefParameter ? "ref " : string.Empty;
                return prefix + (p.Type.GetAnnotatedTypeName(model) ?? p.Type.ToString());
            })));

            if (delegateInfo.Parameters.Count > 0)
            {
                sourceBuilder.Append(", ");
            }
            
            sourceBuilder.Append(delegateInfo.ReturnValue.Type.GetAnnotatedTypeName(model) ?? delegateInfo.ReturnValue.Type.ToString());

            sourceBuilder.Append($">){funcPtrName};");
            sourceBuilder.AppendLine();
        }

        sourceBuilder.AppendLine("        }");

        foreach (DelegateInfo delegateInfo in classInfo.Delegates)
        {
            string returnTypeFullName = delegateInfo.ReturnValue.Type.GetAnnotatedTypeName(model) ?? delegateInfo.ReturnValue.Type.ToString();
            sourceBuilder.AppendLine($"        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            sourceBuilder.Append($"        public static {returnTypeFullName} Call{delegateInfo.Name}(");

            bool firstParameter = true;
            foreach (DelegateParameterInfo parameter in delegateInfo.Parameters)
            {
                if (!firstParameter)
                {
                    sourceBuilder.Append(", ");
                }

                firstParameter = false;

                if (parameter.IsOutParameter)
                {
                    sourceBuilder.Append("out ");
                }

                if (parameter.IsRefParameter)
                {
                    sourceBuilder.Append("ref ");
                }

                string typeFullName = parameter.Type.GetAnnotatedTypeName(model) ?? parameter.Type.ToString();
                sourceBuilder.Append($"{typeFullName} {parameter.Name}");
            }

            sourceBuilder.AppendLine(")");
            sourceBuilder.AppendLine("        {");

            string delegateName = delegateInfo.Name;
            // Non-blittable calli signatures require a runtime-generated marshalling
            // stub. Apple CoreCLR cannot JIT that stub. Marshal UTF-8 strings and
            // delegate inputs explicitly, and pin unmanaged ref/out arguments, so
            // the actual unmanaged call needs no runtime marshalling stub.
            bool IsStringInput(DelegateParameterInfo parameter) =>
                !parameter.IsOutParameter && !parameter.IsRefParameter &&
                model.GetTypeInfo(parameter.Type).Type?.SpecialType == SpecialType.System_String;
            List<DelegateParameterInfo> stringInputs = delegateInfo.Parameters.Where(IsStringInput).ToList();
            bool IsDelegateInput(DelegateParameterInfo parameter) =>
                !parameter.IsOutParameter && !parameter.IsRefParameter &&
                model.GetTypeInfo(parameter.Type).Type?.TypeKind == TypeKind.Delegate;
            List<DelegateParameterInfo> delegateInputs = delegateInfo.Parameters.Where(IsDelegateInput).ToList();
            bool IsPinnedByRef(DelegateParameterInfo parameter) =>
                (parameter.IsOutParameter || parameter.IsRefParameter) &&
                model.GetTypeInfo(parameter.Type).Type?.IsUnmanagedType == true;
            List<DelegateParameterInfo> pinnedInputs = delegateInfo.Parameters.Where(IsPinnedByRef).ToList();
            foreach (DelegateParameterInfo parameter in pinnedInputs.Where(p => p.IsOutParameter))
                sourceBuilder.AppendLine($"            {parameter.Name} = default;");
            foreach (DelegateParameterInfo parameter in pinnedInputs)
                sourceBuilder.AppendLine($"            fixed ({parameter.Type.GetAnnotatedTypeName(model) ?? parameter.Type.ToString()}* {parameter.Name}Native = &{parameter.Name})");
            if (pinnedInputs.Count > 0) sourceBuilder.AppendLine("            {");
            bool needsMarshalling = stringInputs.Count > 0 || delegateInputs.Count > 0;
            const string utf8Marshaller = "global::System.Runtime.InteropServices.Marshalling.Utf8StringMarshaller.ManagedToUnmanagedIn";
            foreach (DelegateParameterInfo parameter in stringInputs)
            {
                sourceBuilder.AppendLine($"            scoped {utf8Marshaller} {parameter.Name}Marshaller = default;");
                sourceBuilder.AppendLine($"            global::System.Span<byte> {parameter.Name}Buffer = stackalloc byte[{utf8Marshaller}.BufferSize];");
            }
            if (needsMarshalling)
            {
                sourceBuilder.AppendLine("            try");
                sourceBuilder.AppendLine("            {");
                foreach (DelegateParameterInfo parameter in stringInputs)
                    sourceBuilder.AppendLine($"                {parameter.Name}Marshaller.FromManaged({parameter.Name}, {parameter.Name}Buffer);");
                foreach (DelegateParameterInfo parameter in delegateInputs)
                    sourceBuilder.AppendLine($"                global::System.IntPtr {parameter.Name}Native = {parameter.Name} is null ? global::System.IntPtr.Zero : global::System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate({parameter.Name});");
            }
            if (needsMarshalling || pinnedInputs.Count > 0)
            {
                string signature = string.Join(", ", delegateInfo.Parameters.Select(p =>
                    IsStringInput(p) ? "byte*" : IsDelegateInput(p) ? "global::System.IntPtr" :
                    IsPinnedByRef(p) ? (p.Type.GetAnnotatedTypeName(model) ?? p.Type.ToString()) + "*" :
                    (p.IsOutParameter ? "out " : p.IsRefParameter ? "ref " : string.Empty) +
                    (p.Type.GetAnnotatedTypeName(model) ?? p.Type.ToString())).Concat(new[] { returnTypeFullName }));
                delegateName = $"((delegate* unmanaged<{signature}>)(void*){delegateName})";
            }

            if (delegateInfo.ReturnValue.Type.ToString() != "void")
            {
                sourceBuilder.Append($"            return {delegateName}(");
            }
            else
            {
                sourceBuilder.Append($"            {delegateName}(");
            }

            sourceBuilder.Append(string.Join(", ", delegateInfo.Parameters.Select(p =>
            {
                string prefix = p.IsOutParameter ? "out " : p.IsRefParameter ? "ref " : string.Empty;
                return IsStringInput(p) ? $"{p.Name}Marshaller.ToUnmanaged()" :
                    IsDelegateInput(p) || IsPinnedByRef(p) ? $"{p.Name}Native" : prefix + p.Name;
            })));

            sourceBuilder.AppendLine(");");
            if (needsMarshalling)
            {
                sourceBuilder.AppendLine("            }");
                sourceBuilder.AppendLine("            finally");
                sourceBuilder.AppendLine("            {");
                foreach (DelegateParameterInfo parameter in stringInputs)
                    sourceBuilder.AppendLine($"                {parameter.Name}Marshaller.Free();");
                foreach (DelegateParameterInfo parameter in delegateInputs)
                    sourceBuilder.AppendLine($"                global::System.GC.KeepAlive({parameter.Name});");
                sourceBuilder.AppendLine("            }");
            }
            if (pinnedInputs.Count > 0) sourceBuilder.AppendLine("            }");
            sourceBuilder.AppendLine("        }");
        }

        // End class definition
        sourceBuilder.AppendLine("    }");
        sourceBuilder.AppendLine("}");

        context.AddSource($"{classInfo.Name}.generated.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
    }

    private static ClassInfo? GetClassInfoOrNull(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return null;
        }

        // Check attribute (support both NativeCallbacks and NativeCallbacksAttribute)
        bool hasNativeCallbacksAttribute = classDeclaration.AttributeLists
            .SelectMany(a => a.Attributes)
            .Any(a => a.Name.ToString() is "NativeCallbacks" or "NativeCallbacksAttribute");

        if (!hasNativeCallbacksAttribute)
        {
            return null;
        }

        string namespaceName = classDeclaration.GetFullNamespace();

        if (string.IsNullOrEmpty(namespaceName))
        {
            return null;
        }

        var classInfo = new ClassInfo
        {
            ClassDeclaration = classDeclaration,
            Name = classDeclaration.Identifier.ValueText,
            Namespace = namespaceName,
            Delegates = new List<DelegateInfo>(),
            NullableAwareable = context.SemanticModel.GetNullableContext(context.Node.Span.Start).HasFlag(NullableContext.AnnotationsEnabled)
        };

        foreach (MemberDeclarationSyntax member in classDeclaration.Members)
        {
            if (member is not FieldDeclarationSyntax fieldDeclaration ||
                fieldDeclaration.Declaration.Type is not FunctionPointerTypeSyntax functionPointerTypeSyntax)
            {
                continue;
            }

            var delegateInfo = new DelegateInfo
            {
                Name = fieldDeclaration.Declaration.Variables.First().Identifier.ValueText,
                Parameters = new List<DelegateParameterInfo>()
            };

            char paramName = 'a';

            for (int i = 0; i < functionPointerTypeSyntax.ParameterList.Parameters.Count; i++)
            {
                FunctionPointerParameterSyntax param = functionPointerTypeSyntax.ParameterList.Parameters[i];

                DelegateParameterInfo parameter = new DelegateParameterInfo
                {
                    Name = paramName.ToString(),
                    IsOutParameter = param.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.OutKeyword)),
                    IsRefParameter = param.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.RefKeyword)),
                    Type = param.Type,
                };

                bool isReturnParameter = i == functionPointerTypeSyntax.ParameterList.Parameters.Count - 1;
                if (isReturnParameter)
                {
                    delegateInfo.ReturnValue = parameter;
                }
                else
                {
                    delegateInfo.Parameters.Add(parameter);
                }

                paramName++;
            }

            classInfo.Delegates.Add(delegateInfo);
        }

        return classInfo;
    }
}

internal struct ClassInfo
{
    public ClassDeclarationSyntax ClassDeclaration;
    public string Name;
    public string Namespace;
    public List<DelegateInfo> Delegates;
    public bool NullableAwareable;
}

internal struct DelegateInfo
{
    public string Name;
    public List<DelegateParameterInfo> Parameters;
    public List<DelegateParameterInfo> ParametersAndReturnValue
    {
        get
        {
            List<DelegateParameterInfo> allParameters = new List<DelegateParameterInfo>(Parameters);

            if (ReturnValue.Type.ToString() != "void")
            {
                allParameters.Add(ReturnValue);
            }

            return allParameters;
        }
    }

    public bool HasReturnValue => ReturnValue.Type.ToString() != "void";
    public DelegateParameterInfo ReturnValue;
}

public struct DelegateParameterInfo
{
    public string Name;
    public TypeSyntax Type;
    public bool IsOutParameter;
    public bool IsRefParameter;
}
