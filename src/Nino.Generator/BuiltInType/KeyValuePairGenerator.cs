// KeyValuePairGenerator.cs
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

public class KeyValuePairGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    NinoGraph ninoGraph,
    HashSet<int> potentialTypeIds,
    HashSet<int> selectedTypeIds,
    bool isUnityAssembly = false) : NinoBuiltInTypeGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly)
{
    protected override string OutputFileName => "NinoKeyValuePairGenerator";

    public override bool Filter(TypeInfoDto typeInfo)
    {
        if (!typeInfo.IsGenericType) return false;
        if (typeInfo.TypeArguments.Length != 2) return false;

        var keyType = typeInfo.TypeArguments[0];
        var valueType = typeInfo.TypeArguments[1];

        if (TypeInfoDtoExtensions.GetKind(keyType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Invalid ||
            TypeInfoDtoExtensions.GetKind(valueType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Invalid)
            return false;

        return typeInfo.Name == "KeyValuePair";
    }

    protected override void GenerateSerializer(TypeInfoDto typeInfo, Writer writer)
    {
        var keyType = typeInfo.TypeArguments[0];
        var valueType = typeInfo.TypeArguments[1];

        // Check if we can use the fast unmanaged write
        // The KVP itself must be unmanaged (both key and value must be unmanaged)
        bool canUseFastPath = typeInfo.IsUnmanagedType &&
                              TypeInfoDtoExtensions.GetKind(keyType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged &&
                              TypeInfoDtoExtensions.GetKind(valueType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged;

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
            writer.Append("    ");
            writer.AppendLine(GetSerializeString(keyType, "value.Key"));
            writer.Append("    ");
            writer.AppendLine(GetSerializeString(valueType, "value.Value"));
        }

        writer.AppendLine("}");
    }

    protected override void GenerateDeserializer(TypeInfoDto typeInfo, Writer writer)
    {
        var keyType = typeInfo.TypeArguments[0];
        var valueType = typeInfo.TypeArguments[1];

        // Check if we can use the fast unmanaged read
        // The KVP itself must be unmanaged (both key and value must be unmanaged)
        bool canUseFastPath = typeInfo.IsUnmanagedType &&
                              typeInfo.SpecialType != SpecialTypeDto.System_Nullable_T &&
                              TypeInfoDtoExtensions.GetKind(keyType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged &&
                              TypeInfoDtoExtensions.GetKind(valueType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged;
        var typeName = typeInfo.DisplayName;

        // Out overload
        WriteAggressiveInlining(writer);
        writer.Append("public static void Deserialize(out ");
        writer.Append(typeInfo.DisplayName);
        writer.AppendLine(" value, ref Reader reader)");
        writer.AppendLine("{");
        EofCheck(writer);

        if (canUseFastPath)
        {
            writer.AppendLine("    reader.Read(out value);");
        }
        else
        {
            writer.Append("    ");
            writer.AppendLine(GetDeserializeString(keyType, "k"));
            writer.Append("    ");
            writer.AppendLine(GetDeserializeString(valueType, "v"));
            writer.Append("    value = new ");
            writer.Append(typeName);
            writer.AppendLine("(k, v);");
        }

        writer.AppendLine("}");
        writer.AppendLine();

        // Ref overload - KeyValuePair is not modifiable
        WriteAggressiveInlining(writer);
        writer.Append("public static void DeserializeRef(ref ");
        writer.Append(typeInfo.DisplayName);
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