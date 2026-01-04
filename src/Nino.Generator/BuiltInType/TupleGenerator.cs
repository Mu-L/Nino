// TupleGenerator.cs
//
//  Author:
//        JasonXuDeveloper <jason@xgamedev.net>
//
//  Copyright (c) 2025 JEngine
//
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
//
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
//
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;
using Nino.Generator.Template;

namespace Nino.Generator.BuiltInType;

public class TupleGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    NinoGraph ninoGraph,
    HashSet<int> potentialTypeIds,
    HashSet<int> selectedTypeIds,
    bool isUnityAssembly = false) : NinoBuiltInTypeGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly)
{
    protected override string OutputFileName => "NinoTupleGenerator";

    public override bool Filter(TypeInfoDto typeInfo)
    {
        if (!typeInfo.IsGenericType)
        {
            return false;
        }

        // Filter empty tuples
        if (typeInfo.TypeArguments.Length == 0)
        {
            return false;
        }

        // Ensure all type arguments are valid
        foreach (var typeArg in typeInfo.TypeArguments)
        {
            if (TypeInfoDtoExtensions.GetKind(typeArg, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Invalid)
            {
                return false;
            }
        }

        var name = typeInfo.Name;
        return name == "ValueTuple" || name == "Tuple";
    }

    protected override void GenerateSerializer(TypeInfoDto typeInfo, Writer writer)
    {
        var types = typeInfo.TypeArguments;

        // Check if we can use the fast unmanaged write
        // All items must be unmanaged AND none can be polymorphic
        bool canUseFastPath = typeInfo.IsUnmanagedType;
        if (canUseFastPath)
        {
            foreach (var itemType in types)
            {
                if (TypeInfoDtoExtensions.GetKind(itemType, NinoGraph, GeneratedTypeIds) != NinoTypeKind.Unmanaged)
                {
                    canUseFastPath = false;
                    break;
                }
            }
        }

        WriteAggressiveInlining(writer);
        writer.Append("public static void Serialize(this ");
        writer.Append(typeInfo.DisplayName);
        writer.AppendLine(" value, ref Writer writer)");
        writer.AppendLine("{");

        if (canUseFastPath)
        {
            writer.AppendLine("    writer.Write(value);");
        }
        else
        {
            for (int i = 0; i < types.Length; i++)
            {
                writer.Append("    ");
                writer.AppendLine(GetSerializeString(types[i], $"value.Item{i + 1}"));
            }
        }

        writer.AppendLine("}");
    }

    protected override void GenerateDeserializer(TypeInfoDto typeInfo, Writer writer)
    {
        var types = typeInfo.TypeArguments;
        bool isValueTuple = typeInfo.Name == "ValueTuple";

        // Check if we can use the fast unmanaged read
        // All items must be unmanaged AND none can be polymorphic
        bool canUseFastPath = typeInfo.IsUnmanagedType &&
                              typeInfo.SpecialType != SpecialTypeDto.System_Nullable_T;
        if (canUseFastPath)
        {
            foreach (var itemType in types)
            {
                if (TypeInfoDtoExtensions.GetKind(itemType, NinoGraph, GeneratedTypeIds) != NinoTypeKind.Unmanaged)
                {
                    canUseFastPath = false;
                    break;
                }
            }
        }

        var typeName = typeInfo.DisplayName;

        // Out overload
        WriteAggressiveInlining(writer);
        writer.Append("public static void Deserialize(out ");
        writer.Append(typeName);
        writer.AppendLine(" value, ref Reader reader)");
        writer.AppendLine("{");
        EofCheck(writer);

        if (canUseFastPath)
        {
            writer.AppendLine("    reader.Read(out value);");
        }
        else
        {
            for (int i = 0; i < types.Length; i++)
            {
                writer.Append("    ");
                writer.AppendLine(GetDeserializeString(types[i], $"item{i + 1}"));
            }

            writer.Append("    value = ");
            if (!isValueTuple)
            {
                writer.Append("new ");
                writer.Append(typeName);
            }

            writer.Append("(");
            for (int i = 0; i < types.Length; i++)
            {
                if (i != 0)
                {
                    writer.Append(", ");
                }

                writer.Append($"item{i + 1}");
            }

            writer.AppendLine(");");
        }

        writer.AppendLine("}");
        writer.AppendLine();

        // Ref overload - tuples are not modifiable
        WriteAggressiveInlining(writer);
        writer.Append("public static void DeserializeRef(ref ");
        writer.Append(typeName);
        writer.AppendLine(" value, ref Reader reader)");

        if (canUseFastPath)
        {
            writer.AppendLine("    => reader.Read(out value);");
        }
        else
        {
            writer.AppendLine("    => Deserialize(out value, ref reader);");
        }
    }
}