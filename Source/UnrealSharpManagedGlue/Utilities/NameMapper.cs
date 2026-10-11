using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EpicGames.Core;
using EpicGames.UHT.Types;
using UnrealSharpManagedGlue.Exporters;
using UnrealSharpManagedGlue.PropertyTranslators;

namespace UnrealSharpManagedGlue.Utilities;

public enum ENameType
{
    Parameter,
    Property,
    Struct,
    Function
}

public static class NameMapper
{
    private static readonly HashSet<string> ReservedKeywords = new()
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", 
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", 
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", 
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while", "System", "buffer",
    };

    public static string GetParameterName(this UhtProperty property)
    {
        string scriptName = ScriptifyName(property.GetScriptName(), ENameType.Parameter);

        if (property.Outer is not UhtFunction function)
        {
            return scriptName;
        }
        
        foreach (UhtProperty exportedProperty in function.Properties)
        {
            if (exportedProperty.HasAllFlags(EPropertyFlags.ReturnParm))
            {
                continue;
            }
            
            if (exportedProperty != property && scriptName == ScriptifyName(exportedProperty.GetScriptName(), ENameType.Parameter))
            {
                return PascalToCamelCase(exportedProperty.SourceName);
            }
        }
        
        return scriptName;
    }
    
    public static string GetPropertyName(this UhtProperty property)
    {
        string propertyName = ScriptifyName(property.GetScriptName(), ENameType.Property);
        if (property.Outer!.SourceName == propertyName || IsAKeyword(propertyName))
        {
            propertyName = $"K2_{propertyName}";
        }

        propertyName = TryResolveConflictingName(property, propertyName);

        // C++ lets a member hide an interface function of the same name, and the fallback above is that same
        // C++ name. C# puts an exported interface function on the class too, so the property has to give way.
        if (property.Outer is UhtClass outerClass
            && FindInterfaceFunction(outerClass, propertyName) is { } interfaceFunction
            && interfaceFunction.CanExportFunction())
        {
            propertyName = $"K2_{propertyName}";
        }

        return propertyName;
    }
    
    public static string GetStructName(this UhtType type)
    {
        if (type.EngineType is UhtEngineType.Interface or UhtEngineType.NativeInterface || type == GeneratorStatics.Factory.Session.UInterface)
        {
            return "I" + type.EngineName;
        }
        
        if (type is UhtClass uhtClass && uhtClass.IsChildOf(GeneratorStatics.BlueprintFunctionLibrary))
        {
            return type.GetScriptName();
        }

        return type.SourceName;
    }

    public static string ExportGetAssemblyName(this UhtType type)
    {
        string structName = type.GetStructName();
        return ExportGetAssemblyName(structName);
    }
    
    public static string ExportGetAssemblyName(string structName)
    {
        return $"typeof({structName}).GetAssemblyName()";
    }
    
    public static string GetFullManagedName(this UhtType type)
    {
        return $"{type.GetNamespace()}.{type.GetStructName()}";
    }
    
    static readonly string[] MetadataKeys = { "ScriptName", "ScriptMethod", "DisplayName" };
    
    public static string GetScriptName(this UhtType type)
    {
        bool OnlyContainsLetters(string str)
        {
            foreach (char c in str)
            {
                if (!char.IsLetter(c) && !char.IsWhiteSpace(c))
                {
                    return false;
                }
            }
            return true;
        }
        
        foreach (var key in MetadataKeys)
        {
            string value = type.GetMetadata(key);
            
            if (string.IsNullOrEmpty(value) || !OnlyContainsLetters(value))
            {
                continue;
            }
            
            // Try remove whitespace from the value
            value = value.Replace(" ", "");
            return value;
        }
        
        return type.SourceName;
    }
    
    public static string GetNamespace(this UhtType typeObj)
    {
        UhtType outer = typeObj;

        string packageShortName = string.Empty;
        if (outer is UhtPackage package)
        {
            packageShortName = package.GetModuleShortName();
        }
        else
        {
            while (outer.Outer != null)
            {
                outer = outer.Outer;
            
                if (outer is UhtPackage header)
                {
                    packageShortName = header.Package.GetModuleShortName();
                    break;
                }
            }
        }
        
        if (string.IsNullOrEmpty(packageShortName))
        {
            throw new Exception($"Failed to find package name for {typeObj}");
        }
        
        return $"UnrealSharp.{packageShortName}";
    }
    
    public static string GetFunctionName(this UhtFunction function)
    {
        // An override must use the interface's managed name, even when its class metadata
        // differs or the interface's DisplayName would otherwise be treated as a collision.
        if (function.Outer is UhtClass classOwner && classOwner.EngineType == UhtEngineType.Class)
        {
            for (UhtClass? currentClass = classOwner; currentClass != null; currentClass = currentClass.SuperClass)
            {
                foreach (UhtClass declaration in currentClass.GetInterfaces())
                {
                    UhtFunction? interfaceFunction = declaration.GetInterfaceAlternateClass().FindFunctionByName(function.SourceName);
                    if (interfaceFunction != null && interfaceFunction.CanExportFunction() && interfaceFunction.HasSameSignature(function))
                    {
                        return interfaceFunction.GetFunctionName();
                    }
                }
            }
        }

        string functionName = function.GetScriptName();

        if (function.HasAnyFlags(EFunctionFlags.Delegate | EFunctionFlags.MulticastDelegate))
        {
            functionName = DelegateBasePropertyTranslator.GetDelegateName(function);
        }
        
        if (functionName.StartsWith("K2_") || functionName.StartsWith("BP_"))
        {
            functionName = functionName.Substring(3);
        }

        if (function.IsInterfaceFunction() && functionName.EndsWith("_Implementation"))
        {
            functionName = functionName.Substring(0, functionName.Length - 15);
        }

        if (function.Outer is not UhtClass)
        {
            return functionName;
        }
        
        functionName = TryResolveConflictingName(function, functionName);

        return functionName;
    }
    
    public static string TryResolveConflictingName(UhtType type, string scriptName)
    {
        UhtType outer = type.Outer!;

        bool IsConflictingWithChild(List<UhtType> children)
        {
            foreach (UhtType child in children)
            {
                if (child == type)
                {
                    continue;
                }
            
                if (child is UhtProperty property)
                {
                    if (scriptName == ScriptifyName(property.GetScriptName(), ENameType.Property))
                    {
                        return true;
                    }
                }
            
                if (child is UhtFunction function)
                {
                    if (scriptName == ScriptifyName(function.GetScriptName(), ENameType.Function))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        
        bool isConflicting = IsConflictingWithChild(outer.Children);
        
        if (!isConflicting && outer is UhtClass outerClass)
        {
            UhtFunction? function = FindInterfaceFunction(outerClass, scriptName);

            if (function != null)
            {
                // C# cannot overload methods by return type alone. A BP_ wrapper may have
                // the same parameters as an interface method but a different return value.
                isConflicting = type is not UhtFunction typeAsFunction || HasSameManagedParameters(function, typeAsFunction);
            }
        }

        return isConflicting ? type.EngineName : scriptName;
    }

    static bool HasSameManagedParameters(UhtFunction function, UhtFunction otherFunction)
    {
        static List<string> GetGenericParameters(UhtFunction candidate)
        {
            List<string> genericParameters = new();
            if (candidate.HasGenericTypeSupport())
            {
                genericParameters.Add("DOT");
            }
            if (candidate.HasCustomStructParamSupport())
            {
                genericParameters.AddRange(candidate.GetCustomStructParamTypes());
            }
            return genericParameters;
        }

        List<string> genericParameters = GetGenericParameters(function);
        List<string> otherGenericParameters = GetGenericParameters(otherFunction);
        if (genericParameters.Count != otherGenericParameters.Count)
        {
            return false;
        }

        static IEnumerable<UhtProperty> GetParameters(UhtFunction candidate) => candidate.Properties.Where(property =>
            !property.HasAnyFlags(EPropertyFlags.ReturnParm) && (property.GetTranslator()?.ShouldBeDeclaredAsParameter ?? true));

        UhtProperty[] parameters = GetParameters(function).ToArray();
        UhtProperty[] otherParameters = GetParameters(otherFunction).ToArray();
        if (parameters.Length != otherParameters.Length)
        {
            return false;
        }

        for (int index = 0; index < parameters.Length; index++)
        {
            UhtProperty parameter = parameters[index];
            UhtProperty otherParameter = otherParameters[index];
            PropertyTranslator? translator = parameter.GetTranslator();
            PropertyTranslator? otherTranslator = otherParameter.GetTranslator();
            if (translator == null || otherTranslator == null)
            {
                return false;
            }
            bool isByReference = FunctionExporter.GetRefQualifier(parameter).Length != 0;
            bool otherIsByReference = FunctionExporter.GetRefQualifier(otherParameter).Length != 0;
            if (isByReference != otherIsByReference ||
                GetSignatureType(parameter, translator, genericParameters) != GetSignatureType(otherParameter, otherTranslator, otherGenericParameters))
            {
                return false;
            }
        }

        return true;
    }

    static string GetSignatureType(UhtProperty property, PropertyTranslator translator, List<string> genericParameters)
    {
        string managedType = translator.GetManagedType(property);
        // These translators append reference annotations. Value-type Nullable<T> and
        // nullable value types from custom translators must remain distinct signatures.
        if (translator is ObjectPropertyTranslator or InterfacePropertyTranslator)
        {
            managedType = managedType.TrimEnd('?');
        }
        else if (translator is ContainerPropertyTranslator && property is UhtContainerBaseProperty container)
        {
            foreach (UhtProperty innerProperty in container.GetInnerProperties())
            {
                PropertyTranslator innerTranslator = innerProperty.GetTranslator()!;
                managedType = managedType.Replace(innerTranslator.GetManagedType(innerProperty),
                    GetSignatureType(innerProperty, innerTranslator, new()), StringComparison.Ordinal);
            }
        }
        // Generic parameter names do not affect signatures; their ordinal positions do.
        for (int index = 0; index < genericParameters.Count; ++index)
        {
            managedType = Regex.Replace(managedType, $@"\b{genericParameters[index]}\b", $"__Generic{index}");
        }
        return managedType;
    }

    static UhtFunction? FindInterfaceFunction(UhtClass classObj, string scriptName)
    {
        foreach (UhtClass classInterface in classObj.GetInterfaces())
        {
            if (classInterface.AlternateObject is not UhtClass interfaceClass)
            {
                continue;
            }

            UhtFunction? function = interfaceClass.Functions.FirstOrDefault(candidate => candidate.CanExportFunction()
                && (string.Equals(candidate.SourceName, scriptName, StringComparison.InvariantCultureIgnoreCase)
                    || candidate.GetFunctionName() == scriptName));

            if (function != null)
            {
                return function;
            }
        }

        return null;
    }

    public static string PrefixWithOuterName(this UhtType type, string name)
    {
        if (type.Outer == null)
        {
            return name;
        }
        
        return $"{type.Outer.SourceName}_{name}";
    }

    public static string ScriptifyName(string engineName, ENameType nameType)
    {
        string strippedName = engineName;
        switch (nameType)
        {
            case ENameType.Parameter:
                strippedName = StripPropertyPrefix(strippedName);
                strippedName = PascalToCamelCase(strippedName);
                break;
            case ENameType.Property:
                strippedName = StripPropertyPrefix(strippedName);
                break;
            case ENameType.Struct:
                break;
            case ENameType.Function:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(nameType), nameType, null);
        }
        
        return EscapeKeywords(strippedName);
    }
    
    public static string StripPropertyPrefix(string inName)
    {
        int nameOffset = 0;

        while (true)
        {
            // Strip the "b" prefix from bool names
            if (inName.Length - nameOffset >= 2 && inName[nameOffset] == 'b' && char.IsUpper(inName[nameOffset + 1]))
            {
                nameOffset += 1;
                continue;
            }

            // Strip the "In" prefix from names
            if (inName.Length - nameOffset >= 3 && inName[nameOffset] == 'I' && inName[nameOffset + 1] == 'n' && char.IsUpper(inName[nameOffset + 2]))
            {
                nameOffset += 2;
                continue;
            }
            break;
        }

        return nameOffset != 0 ? inName.Substring(nameOffset) : inName;
    }
    
    public static string EscapeKeywords(string name)
    {
        return IsAKeyword(name) || char.IsDigit(name[0]) ? $"_{name}" : name;
    }
    
    private static bool IsAKeyword(string name)
    {
        return ReservedKeywords.Contains(name);
    }
    
    private static string PascalToCamelCase(string name)
    {
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
