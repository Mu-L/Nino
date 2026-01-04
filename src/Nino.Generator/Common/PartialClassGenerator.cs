using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;
using Nino.Generator.Template;

namespace Nino.Generator.Common;

public class PartialClassGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    NinoGraph ninoGraph,
    EquatableArray<NinoType> ninoTypes)
    : NinoCommonGenerator(typeInfoCache, assemblyNamespace, ninoGraph, ninoTypes)
{
    string WriteMembers(string currentAssemblyName, HashSet<string> generatedTypes, NinoType type)
    {
        //ensure type is in this compilation, not from referenced assemblies
        if (type.TypeInfo.ContainingAssemblyName != currentAssemblyName)
        {
            return "";
        }

        var sb = new StringBuilder();
        bool hasPrivateMembers = false;

        try
        {
            foreach (var typeMember in type.Members)
            {
                var name = typeMember.Name;
                var declaredType = typeMember.Type;
                var isPrivate = typeMember.IsPrivate;
                var isProperty = typeMember.IsProperty;

                if (!isPrivate)
                {
                    continue;
                }

                hasPrivateMembers = true;
                var accessor = $$$"""
                                          [Nino.Core.NinoPrivateProxy(nameof({{{name}}}), {{{isProperty.ToString().ToLower()}}})]
                                          public new {{{declaredType.DisplayName}}} __nino__generated__{{{name}}}
                                          {
                                              [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                              get => {{{name}}};
                                              [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                              set => {{{name}}} = value;
                                          }
                                  """;
                sb.AppendLine(accessor);
            }
        }
        catch (Exception e)
        {
            sb.AppendLine($"/* Error: {e.Message} for type {type.TypeInfo.FullyQualifiedName}");
            //add stacktrace
            foreach (var line in (e.StackTrace ?? "").Split('\n'))
            {
                sb.AppendLine($" * {line}");
            }

            //end error
            sb.AppendLine(" */");
        }

        if (!hasPrivateMembers)
        {
            return "";
        }

        var typeInfo = type.TypeInfo;
        var hasNamespace = !string.IsNullOrEmpty(typeInfo.ContainingNamespace) && typeInfo.ContainingNamespace != "<global namespace>";
        var typeNamespace = typeInfo.ContainingNamespace;

        // Build type modifier string (public/internal partial class/struct)
        var accessibility = typeInfo.Accessibility switch
        {
            AccessibilityDto.Public => "public",
            AccessibilityDto.Internal => "internal",
            AccessibilityDto.ProtectedOrInternal => "protected internal",
            _ => "internal"
        };

        var typeKind = typeInfo.Kind switch
        {
            TypeKindDto.Struct => "struct",
            TypeKindDto.Class => "class",
            _ => "class"
        };

        var modifier = $"{accessibility} partial {typeKind}";

        // Get type name with generic parameters
        var typeSimpleName = typeInfo.DisplayName;
        // Extract simple name from fully qualified (take last part)
        var lastDot = typeSimpleName.LastIndexOf('.');
        if (lastDot >= 0)
        {
            typeSimpleName = typeSimpleName.Substring(lastDot + 1);
        }

        if (!generatedTypes.Add(typeSimpleName))
        {
            return "";
        }

        var order = string.Join(", ", type.Members.Select(m => $"nameof({m.Name})"));

        var namespaceStr = hasNamespace ? $"namespace {typeNamespace}\n" : "";
        if (hasNamespace)
        {
            namespaceStr += "{";
        }

        // generate code
        var code = $$"""
                     {{namespaceStr}}
                     #if !NET8_0_OR_GREATER
                         [Nino.Core.NinoExplicitOrder({{order}})]
                         {{modifier}} {{typeSimpleName}}
                         {
                     {{sb}}    }
                     #endif
                     """;
        if (hasNamespace)
        {
            code += "\n}";
        }

        return code;
    }

    protected override void Generate(SourceProductionContext spc)
    {
        HashSet<string> generatedTypes = new();
        List<string> generatedCode = new();

        // Extract current assembly name from the first type (they should all be from same assembly)
        var currentAssemblyName = NinoTypes.Length > 0 ? NinoTypes[0].TypeInfo.ContainingAssemblyName : "";

        foreach (var ninoType in NinoTypes)
        {
            bool isPolymorphicType = ninoType.IsPolymorphic;

            // check if struct is unmanaged
            if (ninoType.TypeInfo.IsUnmanagedType && !isPolymorphicType)
            {
                continue;
            }

            if (NinoGraph.SubTypes.TryGetValue(ninoType, out var lst))
            {
                //sort lst by how deep the inheritance is (i.e. how many levels of inheritance), the deepest first
                var sortedList = new List<NinoType>(lst);
                sortedList.Sort((a, b) =>
                {
                    // Use TryGetValue to avoid KeyNotFoundException during concurrent execution
                    int aCount = NinoGraph.BaseTypes.TryGetValue(a, out var aBaseTypes) ? aBaseTypes.Count : 0;
                    int bCount = NinoGraph.BaseTypes.TryGetValue(b, out var bBaseTypes) ? bBaseTypes.Count : 0;
                    return bCount.CompareTo(aCount);
                });

                foreach (var subType in sortedList)
                {
                    if (TypeInfoDtoExtensions.IsInstanceType(subType.TypeInfo))
                    {
                        if (subType.TypeInfo.IsUnmanagedType)
                        {
                            continue;
                        }

                        generatedCode.Add(WriteMembers(currentAssemblyName, generatedTypes, subType));
                    }
                }
            }

            if (TypeInfoDtoExtensions.IsInstanceType(ninoType.TypeInfo))
            {
                if (ninoType.TypeInfo.IsUnmanagedType)
                {
                    continue;
                }

                generatedCode.Add(WriteMembers(currentAssemblyName, generatedTypes, ninoType));
            }
        }

        var code = $$"""
                     // <auto-generated/>
                     #nullable disable
                     #pragma warning disable CS0109, CS8669
                     using System;
                     using System.Runtime.CompilerServices;

                     {{string.Join("\n", generatedCode.Where(c => !string.IsNullOrEmpty(c)))}}
                     """;
        spc.AddSource($"{AssemblyNamespace}.PartialClass.g.cs", code);
    }
}