using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;
using Nino.Generator.Template;

namespace Nino.Generator.Common;

public class UnsafeAccessorGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    NinoGraph ninoGraph,
    EquatableArray<NinoType> ninoTypes)
    : NinoCommonGenerator(typeInfoCache, assemblyNamespace, ninoGraph, ninoTypes)
{
    protected override void Generate(SourceProductionContext spc)
    {
        var sb = new StringBuilder();
        var generatedTypes = new HashSet<int>();
        var generatedMembers = new HashSet<(string, string)>();

        foreach (var ninoType in NinoTypes)
        {
            try
            {
                bool isPolymorphicType = ninoType.IsPolymorphic;

                // check if struct is unmanaged
                if (ninoType.TypeInfo.IsUnmanagedType && !isPolymorphicType)
                {
                    continue;
                }

                void WriteMembers(NinoType type)
                {
                    if (!generatedTypes.Add(type.TypeInfo.TypeId))
                    {
                        return;
                    }

                    foreach (var member in type.Members)
                    {
                        if (!member.IsPrivate)
                        {
                            continue;
                        }

                        // Use the type's TypeInfo as the containing type
                        string typeName = type.TypeInfo.DisplayName;

                        if (!generatedMembers.Add((typeName, member.Name)))
                        {
                            continue;
                        }

                        if (type.TypeInfo.IsValueType)
                        {
                            typeName = $"ref {typeName}";
                        }

                        var name = member.Name;
                        var declaredType = member.Type;

                        if (member.IsProperty)
                        {
                            sb.AppendLine(
                                $"        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = \"get_{name}\")]");
                            sb.AppendLine(
                                $"        internal extern static {declaredType.DisplayName} __get__{name}__({typeName} @this);");

                            sb.AppendLine(
                                $"        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = \"set_{name}\")]");
                            sb.AppendLine(
                                $"        internal extern static void __set__{name}__({typeName} @this, {declaredType.DisplayName} value);");
                        }
                        else
                        {
                            sb.AppendLine(
                                $"        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = \"{name}\")]");
                            sb.AppendLine(
                                $"        internal extern static ref {declaredType.DisplayName} __{name}__({typeName} @this);");
                        }
                    }
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
                        var subTypeInfo = subType.TypeInfo;
                        if (TypeInfoDtoExtensions.IsInstanceType(subTypeInfo))
                        {
                            if (subTypeInfo.IsUnmanagedType)
                            {
                                continue;
                            }

                            WriteMembers(subType);
                        }
                    }
                }

                if (TypeInfoDtoExtensions.IsInstanceType(ninoType.TypeInfo))
                {
                    if (ninoType.TypeInfo.IsUnmanagedType)
                    {
                        continue;
                    }

                    WriteMembers(ninoType);
                }
            }
            catch (Exception e)
            {
                sb.AppendLine($"/* Error: {e.Message} for type {ninoType.TypeInfo.FullyQualifiedName}");
                //add stacktrace
                foreach (var line in (e.StackTrace ?? "").Split('\n'))
                {
                    sb.AppendLine($" * {line}");
                }

                //end error
                sb.AppendLine(" */");
            }
        }

        var curNamespace = AssemblyNamespace;

        // generate code
        var code = $$"""
                     // <auto-generated/>
                     #nullable disable

                     using System;
                     using System.Runtime.CompilerServices;

                     #if NET8_0_OR_GREATER
                     namespace {{curNamespace}}
                     {
                         internal static partial class PrivateAccessor
                         {
                     {{sb}}    }
                     }
                     #endif
                     """;

        spc.AddSource($"{curNamespace}.PrivateAccessor.g.cs", code);
    }
}