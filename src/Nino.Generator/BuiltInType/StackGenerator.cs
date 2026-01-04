// StackGenerator.cs
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

public class StackGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    NinoGraph ninoGraph,
    HashSet<int> potentialTypeIds,
    HashSet<int> selectedTypeIds,
    bool isUnityAssembly = false) : NinoBuiltInTypeGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly)
{
    protected override string OutputFileName => "NinoStackTypeGenerator";

    public override bool Filter(TypeInfoDto typeInfo)
    {
        if (!typeInfo.IsGenericType) return false;
        if (typeInfo.TypeArguments.Length != 1) return false;

        // Accept Stack<T> and ConcurrentStack<T>
        var originalDef = typeInfo.GenericOriginalDefinition;
        if (originalDef != "System.Collections.Generic.Stack<T>" &&
            originalDef != "System.Collections.Concurrent.ConcurrentStack<T>")
            return false;

        var elementType = typeInfo.TypeArguments[0];

        // Element type must be valid
        if (TypeInfoDtoExtensions.GetKind(elementType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Invalid)
            return false;

        return true;
    }

    protected override void GenerateSerializer(TypeInfoDto typeInfo, Writer writer)
    {
        var elementType = typeInfo.TypeArguments[0];

        var typeName = typeInfo.DisplayName;

        // Check if element is unmanaged (no WeakVersionTolerance needed)
        bool isUnmanaged = TypeInfoDtoExtensions.GetKind(elementType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged;

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
        writer.AppendLine("    if (cnt == 0)");
        writer.AppendLine("    {");
        writer.AppendLine("        return;");
        writer.AppendLine("    }");
        writer.AppendLine();

        var originalDef = typeInfo.GenericOriginalDefinition;
        bool isStack = originalDef == "System.Collections.Generic.Stack<T>";

        if (isStack)
        {
            var stackViewTypeName = $"Nino.Core.Internal.StackView<{elementType.DisplayName}>";
            writer.Append("    ref var stack = ref System.Runtime.CompilerServices.Unsafe.As<");
            writer.Append(typeName);
            writer.Append(", ");
            writer.Append(stackViewTypeName);
            writer.AppendLine(">(ref value);");
            writer.AppendLine("    var array = stack._array;");
            writer.AppendLine("    if (array == null)");
            writer.AppendLine("    {");
            writer.AppendLine("        return;");
            writer.AppendLine("    }");

            // Use ref iteration for all types to eliminate bounds checks
            writer.AppendLine("    // Stack serializes from top to bottom with ref iteration");
            writer.AppendLine("#if NET5_0_OR_GREATER");
            writer.AppendLine("    ref var cur = ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(array), cnt - 1);");
            writer.AppendLine("    ref var end = ref System.Runtime.CompilerServices.Unsafe.Subtract(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(array), 1);");
            writer.AppendLine("#else");
            writer.AppendLine("    ref var cur = ref array[cnt - 1];");
            writer.AppendLine("    ref var end = ref System.Runtime.CompilerServices.Unsafe.Subtract(ref array[0], 1);");
            writer.AppendLine("#endif");
            writer.AppendLine("    do");
            writer.AppendLine("    {");

            if (!isUnmanaged)
            {
                IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                    w => { w.AppendLine("        var pos = writer.Advance(4);"); });
            }

            writer.Append("        ");
            writer.AppendLine(GetSerializeString(elementType, "cur"));

            if (!isUnmanaged)
            {
                IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                    w => { w.AppendLine("        writer.PutLength(pos);"); });
            }

            writer.AppendLine("        cur = ref System.Runtime.CompilerServices.Unsafe.Subtract(ref cur, 1);");
            writer.AppendLine("    }");
            writer.AppendLine("    while (System.Runtime.CompilerServices.Unsafe.IsAddressGreaterThan(ref cur, ref end));");
        }
        else
        {
            // Fallback to foreach for ConcurrentStack
            writer.AppendLine("    foreach (var item in value)");
            writer.AppendLine("    {");

            if (!isUnmanaged)
            {
                IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                    w => { w.AppendLine("        var pos = writer.Advance(4);"); });
            }

            writer.Append("        ");
            writer.AppendLine(GetSerializeString(elementType, "item"));

            if (!isUnmanaged)
            {
                IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                    w => { w.AppendLine("        writer.PutLength(pos);"); });
            }

            writer.AppendLine("    }");
        }

        writer.AppendLine("}");
    }

    protected override void GenerateDeserializer(TypeInfoDto typeInfo, Writer writer)
    {
        var elementType = typeInfo.TypeArguments[0];
        var elemType = elementType.DisplayName;
        var typeName = typeInfo.DisplayName;

        // Check if element is unmanaged (no WeakVersionTolerance needed)
        bool isUnmanaged = TypeInfoDtoExtensions.GetKind(elementType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged;

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

        writer.AppendLine("    // Stack is LIFO, so we need to read into array then push in reverse order");
        writer.Append("    var temp = System.Buffers.ArrayPool<");
        writer.Append(elemType);
        writer.AppendLine(">.Shared.Rent(length);");
        writer.AppendLine("    try");
        writer.AppendLine("    {");
        writer.AppendLine("        for (int i = 0; i < length; i++)");
        writer.AppendLine("        {");

        if (isUnmanaged)
        {
            writer.Append("            ");
            writer.AppendLine(GetDeserializeString(elementType, "temp[i]", isOutVariable: false));
        }
        else
        {
            IfElseDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                w =>
                {
                    w.AppendLine("            eleReader = reader.Slice();");
                    w.Append("            ");
                    w.AppendLine(GetDeserializeString(elementType, "temp[i]", isOutVariable: false, readerName: "eleReader"));
                },
                w =>
                {
                    w.Append("            ");
                    w.AppendLine(GetDeserializeString(elementType, "temp[i]", isOutVariable: false));
                });
        }

        writer.AppendLine("        }");
        writer.AppendLine();

        // ConcurrentStack doesn't support capacity parameter, only Stack does
        var originalDef = typeInfo.GenericOriginalDefinition;
        bool isConcurrentStack = originalDef == "System.Collections.Concurrent.ConcurrentStack<T>";

        writer.Append("        value = new ");
        writer.Append(typeName);
        writer.AppendLine(isConcurrentStack ? "();" : "(length);");
        writer.AppendLine("        for (int i = length - 1; i >= 0; i--)");
        writer.AppendLine("        {");
        writer.AppendLine("            value.Push(temp[i]);");
        writer.AppendLine("        }");
        writer.AppendLine("    }");
        writer.AppendLine("    finally");
        writer.AppendLine("    {");
        writer.Append("        System.Buffers.ArrayPool<");
        writer.Append(elemType);
        writer.AppendLine(">.Shared.Return(temp);");
        writer.AppendLine("    }");

        writer.AppendLine("}");
        writer.AppendLine();

        // Ref overload - clear and repopulate
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

        writer.AppendLine("    // Stack is LIFO, so we need to read into array then push in reverse order");
        writer.Append("    var temp = System.Buffers.ArrayPool<");
        writer.Append(elemType);
        writer.AppendLine(">.Shared.Rent(length);");
        writer.AppendLine("    try");
        writer.AppendLine("    {");
        writer.AppendLine("        for (int i = 0; i < length; i++)");
        writer.AppendLine("        {");

        if (isUnmanaged)
        {
            writer.Append("            ");
            writer.AppendLine(GetDeserializeString(elementType, "temp[i]", isOutVariable: false));
        }
        else
        {
            IfElseDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                w =>
                {
                    w.AppendLine("            eleReader = reader.Slice();");
                    w.Append("            ");
                    w.AppendLine(GetDeserializeString(elementType, "temp[i]", isOutVariable: false, readerName: "eleReader"));
                },
                w =>
                {
                    w.Append("            ");
                    w.AppendLine(GetDeserializeString(elementType, "temp[i]", isOutVariable: false));
                });
        }

        writer.AppendLine("        }");
        writer.AppendLine();
        writer.AppendLine("        // Initialize if null, otherwise clear");
        writer.AppendLine("        if (value == null)");
        writer.AppendLine("        {");
        writer.Append("            value = new ");
        writer.Append(typeName);
        writer.AppendLine(isConcurrentStack ? "();" : "(length);");
        writer.AppendLine("        }");
        writer.AppendLine("        else");
        writer.AppendLine("        {");
        writer.AppendLine("            value.Clear();");
        writer.AppendLine("        }");
        writer.AppendLine();
        writer.AppendLine("        for (int i = length - 1; i >= 0; i--)");
        writer.AppendLine("        {");
        writer.AppendLine("            value.Push(temp[i]);");
        writer.AppendLine("        }");
        writer.AppendLine("    }");
        writer.AppendLine("    finally");
        writer.AppendLine("    {");
        writer.Append("        System.Buffers.ArrayPool<");
        writer.Append(elemType);
        writer.AppendLine(">.Shared.Return(temp);");
        writer.AppendLine("    }");

        writer.AppendLine("}");
    }
}
