// PriorityQueueGenerator.cs
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
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;
using Nino.Generator.Template;

namespace Nino.Generator.BuiltInType;

public class PriorityQueueGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    NinoGraph ninoGraph,
    HashSet<int> potentialTypeIds,
    HashSet<int> selectedTypeIds,
    bool isUnityAssembly = false) : NinoBuiltInTypeGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly)
{
    protected override string OutputFileName => "NinoPriorityQueueTypeGenerator";

    public override bool Filter(TypeInfoDto typeInfo)
    {
        if (!typeInfo.IsGenericType) return false;
        if (typeInfo.TypeArguments.Length != 2) return false;

        // Accept PriorityQueue<TElement, TPriority>
        var originalDef = typeInfo.GenericOriginalDefinition;
        if (originalDef != "System.Collections.Generic.PriorityQueue<TElement, TPriority>")
            return false;

        var elementType = typeInfo.TypeArguments[0];
        var priorityType = typeInfo.TypeArguments[1];

        // Both element and priority types must be valid
        if (TypeInfoDtoExtensions.GetKind(elementType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Invalid ||
            TypeInfoDtoExtensions.GetKind(priorityType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Invalid)
            return false;

        return true;
    }

    protected override void GenerateSerializer(TypeInfoDto typeInfo, Writer writer)
    {
        var elementType = typeInfo.TypeArguments[0];
        var priorityType = typeInfo.TypeArguments[1];

        var typeName = typeInfo.DisplayName;

        // Check if both element and priority are unmanaged (no WeakVersionTolerance needed)
        bool isUnmanaged = TypeInfoDtoExtensions.GetKind(elementType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged &&
                          TypeInfoDtoExtensions.GetKind(priorityType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged;

        WriteAggressiveInlining(writer);
        writer.Append("public static void Serialize(this ");
        writer.Append(typeName);
        writer.AppendLine(" value, ref Writer writer)");
        writer.AppendLine("{");

        writer.AppendLine("    if (value == null)");
        writer.AppendLine("    {");
        writer.AppendLine("        writer.Write(TypeCollector.NullCollection);");
        writer.AppendLine("        return;");
        writer.AppendLine("    }");
        writer.AppendLine();

        writer.AppendLine("    int cnt = value.Count;");
        writer.AppendLine("    writer.Write(TypeCollector.GetCollectionHeader(cnt));");
        writer.AppendLine();

        // Use UnorderedItems to serialize all items with their priorities
        writer.AppendLine("    foreach (var item in value.UnorderedItems)");
        writer.AppendLine("    {");

        if (!isUnmanaged)
        {
            IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                w => { w.AppendLine("        var pos = writer.Advance(4);"); });
        }

        writer.Append("        ");
        writer.AppendLine(GetSerializeString(elementType, "item.Element"));
        writer.Append("        ");
        writer.AppendLine(GetSerializeString(priorityType, "item.Priority"));

        if (!isUnmanaged)
        {
            IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                w => { w.AppendLine("        writer.PutLength(pos);"); });
        }

        writer.AppendLine("    }");

        writer.AppendLine("}");
    }

    protected override void GenerateDeserializer(TypeInfoDto typeInfo, Writer writer)
    {
        var elementType = typeInfo.TypeArguments[0];
        var priorityType = typeInfo.TypeArguments[1];

        var typeName = typeInfo.DisplayName;

        // Check if both element and priority are unmanaged (no WeakVersionTolerance needed)
        bool isUnmanaged = TypeInfoDtoExtensions.GetKind(elementType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged &&
                          TypeInfoDtoExtensions.GetKind(priorityType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged;

        // Out overload
        WriteAggressiveInlining(writer);
        writer.Append("public static void Deserialize(out ");
        writer.Append(typeName);
        writer.AppendLine(" value, ref Reader reader)");
        writer.AppendLine("{");
        EofCheck(writer);

        writer.AppendLine();
        writer.AppendLine("    if (!reader.ReadCollectionHeader(out var length))");
        writer.AppendLine("    {");
        writer.AppendLine("        value = default;");
        writer.AppendLine("        return;");
        writer.AppendLine("    }");
        writer.AppendLine();

        if (!isUnmanaged)
        {
            IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                w => { w.AppendLine("    Reader eleReader;"); });
            writer.AppendLine();
        }

        writer.Append("    value = new ");
        writer.Append(typeName);
        writer.AppendLine("(length);");
        writer.AppendLine("    for (int i = 0; i < length; i++)");
        writer.AppendLine("    {");

        if (isUnmanaged)
        {
            writer.Append("        ");
            writer.AppendLine(GetDeserializeString(elementType, "element", isOutVariable: true));
            writer.Append("        ");
            writer.AppendLine(GetDeserializeString(priorityType, "priority", isOutVariable: true));
        }
        else
        {
            IfElseDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                w =>
                {
                    w.AppendLine("        eleReader = reader.Slice();");
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(elementType, "element", isOutVariable: true, readerName: "eleReader"));
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(priorityType, "priority", isOutVariable: true, readerName: "eleReader"));
                },
                w =>
                {
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(elementType, "element", isOutVariable: true));
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(priorityType, "priority", isOutVariable: true));
                });
        }

        writer.AppendLine("        value.Enqueue(element, priority);");
        writer.AppendLine("    }");

        writer.AppendLine("}");
        writer.AppendLine();

        // Ref overload
        WriteAggressiveInlining(writer);
        writer.Append("public static void DeserializeRef(ref ");
        writer.Append(typeName);
        writer.AppendLine(" value, ref Reader reader)");
        writer.AppendLine("{");

        EofCheck(writer);

        writer.AppendLine();
        writer.AppendLine("    if (!reader.ReadCollectionHeader(out var length))");
        writer.AppendLine("    {");
        writer.AppendLine("        value = null;");
        writer.AppendLine("        return;");
        writer.AppendLine("    }");
        writer.AppendLine();

        if (!isUnmanaged)
        {
            IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                w => { w.AppendLine("    Reader eleReader;"); });
            writer.AppendLine();
        }

        writer.AppendLine("    // Initialize if null, otherwise clear");
        writer.AppendLine("    if (value == null)");
        writer.AppendLine("    {");
        writer.Append("        value = new ");
        writer.Append(typeName);
        writer.AppendLine("(length);");
        writer.AppendLine("    }");
        writer.AppendLine("    else");
        writer.AppendLine("    {");
        writer.AppendLine("        value.Clear();");
        writer.AppendLine("    }");
        writer.AppendLine();
        writer.AppendLine("    for (int i = 0; i < length; i++)");
        writer.AppendLine("    {");

        if (isUnmanaged)
        {
            writer.Append("        ");
            writer.AppendLine(GetDeserializeString(elementType, "element", isOutVariable: true));
            writer.Append("        ");
            writer.AppendLine(GetDeserializeString(priorityType, "priority", isOutVariable: true));
        }
        else
        {
            IfElseDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                w =>
                {
                    w.AppendLine("        eleReader = reader.Slice();");
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(elementType, "element", isOutVariable: true, readerName: "eleReader"));
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(priorityType, "priority", isOutVariable: true, readerName: "eleReader"));
                },
                w =>
                {
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(elementType, "element", isOutVariable: true));
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(priorityType, "priority", isOutVariable: true));
                });
        }

        writer.AppendLine("        value.Enqueue(element, priority);");
        writer.AppendLine("    }");

        writer.AppendLine("}");
    }
}
