// DictionaryGenerator.cs
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

public class DictionaryGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    NinoGraph ninoGraph,
    HashSet<int> potentialTypeIds,
    HashSet<int> selectedTypeIds,
    bool isUnityAssembly = false) : NinoBuiltInTypeGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly)
{
    protected override string OutputFileName => "NinoDictionaryTypeGenerator";

    public override bool Filter(TypeInfoDto typeInfo)
    {
        if (!typeInfo.IsGenericType) return false;
        if (typeInfo.TypeArguments.Length != 2) return false;

        // Accept Dictionary<,>, ConcurrentDictionary<,>, IDictionary<,>, SortedDictionary<,>, SortedList<,>, and ReadOnlyDictionary<,>
        var originalDef = typeInfo.GenericOriginalDefinition;
        if (originalDef != "System.Collections.Generic.Dictionary<TKey, TValue>" &&
            originalDef != "System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>" &&
            originalDef != "System.Collections.Generic.IDictionary<TKey, TValue>" &&
            originalDef != "System.Collections.Generic.SortedDictionary<TKey, TValue>" &&
            originalDef != "System.Collections.Generic.SortedList<TKey, TValue>" &&
            originalDef != "System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>")
            return false;

        var keyType = typeInfo.TypeArguments[0];
        var valueType = typeInfo.TypeArguments[1];

        // Both key and value types must be valid
        if (TypeInfoDtoExtensions.GetKind(keyType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Invalid ||
            TypeInfoDtoExtensions.GetKind(valueType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Invalid)
            return false;

        return true;
    }

    protected override void GenerateSerializer(TypeInfoDto typeInfo, Writer writer)
    {
        var keyType = typeInfo.TypeArguments[0];
        var valueType = typeInfo.TypeArguments[1];

        var typeName = typeInfo.DisplayName;

        // Check if KVP is unmanaged (for fast path, no WeakVersionTolerance needed)
        bool kvpIsUnmanaged = TypeInfoDtoExtensions.GetKind(keyType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged &&
                              TypeInfoDtoExtensions.GetKind(valueType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged;

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
        bool isDictionary = originalDef == "System.Collections.Generic.Dictionary<TKey, TValue>";

        if (isDictionary)
        {
            var dictViewTypeName = $"Nino.Core.Internal.DictionaryView<{keyType.DisplayName}, {valueType.DisplayName}>"; 
            writer.Append("    ref var dict = ref System.Runtime.CompilerServices.Unsafe.As<");
            writer.Append(typeName);
            writer.Append(", ");
            writer.Append(dictViewTypeName);
            writer.AppendLine(">(ref value);");
            writer.AppendLine("    var entries = dict._entries;");
            writer.AppendLine("    if (entries == null)");
            writer.AppendLine("    {");
            writer.AppendLine("        return;");
            writer.AppendLine("    }");
            writer.AppendLine("    // Iterate entries via direct ref to avoid bounds checks");
            writer.AppendLine("#if NET5_0_OR_GREATER");
            writer.AppendLine("    ref var entryRef = ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(entries);");
            writer.AppendLine("#else");
            writer.AppendLine("    ref var entryRef = ref entries[0];");
            writer.AppendLine("#endif");
            writer.AppendLine("    int index = 0;");
            writer.AppendLine("    while ((uint)index < (uint)cnt)");
            writer.AppendLine("    {");
            writer.AppendLine("        ref var entry = ref System.Runtime.CompilerServices.Unsafe.Add(ref entryRef, index++);");
            writer.AppendLine("        if (entry.next < -1)");
            writer.AppendLine("        {");
            writer.AppendLine("            continue;");
            writer.AppendLine("        }");

            if (kvpIsUnmanaged)
            {
                writer.Append("        ref var kvp = ref System.Runtime.CompilerServices.Unsafe.As<");
                writer.Append(keyType.DisplayName);
                writer.Append(", System.Collections.Generic.KeyValuePair<");
                writer.Append(keyType.DisplayName);
                writer.Append(", ");
                writer.Append(valueType.DisplayName);
                writer.AppendLine(">>(ref entry.key);");
                writer.AppendLine("        writer.UnsafeWrite(kvp);");
            }
            else
            {
                // For managed KVP, serialize Key and Value separately with WeakVersionTolerance
                IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                    w => { w.AppendLine("        var pos = writer.Advance(4);"); });

                writer.Append("        ");
                writer.AppendLine(GetSerializeString(keyType, "entry.key"));
                writer.Append("        ");
                writer.AppendLine(GetSerializeString(valueType, "entry.value"));

                IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                    w => { w.AppendLine("        writer.PutLength(pos);"); });
            }

            writer.AppendLine("    }");
        }
        else
        {
            writer.AppendLine("    foreach (var item in value)");
            writer.AppendLine("    {");

            if (kvpIsUnmanaged)
            {
                // For unmanaged KVP, use UnsafeWrite directly (no WeakVersionTolerance needed)
                writer.AppendLine("        writer.UnsafeWrite(item);");
            }
            else
            {
                // For managed KVP, serialize Key and Value separately with WeakVersionTolerance
                IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                    w => { w.AppendLine("        var pos = writer.Advance(4);"); });

                writer.Append("        ");
                writer.AppendLine(GetSerializeString(keyType, "item.Key"));
                writer.Append("        ");
                writer.AppendLine(GetSerializeString(valueType, "item.Value"));

                IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                    w => { w.AppendLine("        writer.PutLength(pos);"); });
            }

            writer.AppendLine("    }");
        }

        writer.AppendLine("}");
    }

    protected override void GenerateDeserializer(TypeInfoDto typeInfo, Writer writer)
    {
        var keyType = typeInfo.TypeArguments[0];
        var valueType = typeInfo.TypeArguments[1];

        // Check type
        var originalDef = typeInfo.GenericOriginalDefinition;
        bool isIDictionary = originalDef == "System.Collections.Generic.IDictionary<TKey, TValue>";
        bool isReadOnlyDictionary = originalDef == "System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>";

        // For IDictionary, use Dictionary as the concrete type
        var typeName = typeInfo.DisplayName;
        var concreteTypeName = isIDictionary
            ? $"System.Collections.Generic.Dictionary<{keyType.DisplayName}, {valueType.DisplayName}>"
            : typeName;

        // Check if KVP is unmanaged (for fast path, no WeakVersionTolerance needed)
        var kvpTypeName = $"System.Collections.Generic.KeyValuePair<{keyType.DisplayName}, {valueType.DisplayName}>";
        bool kvpIsUnmanaged = TypeInfoDtoExtensions.GetKind(keyType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged &&
                              TypeInfoDtoExtensions.GetKind(valueType, NinoGraph, GeneratedTypeIds) == NinoTypeKind.Unmanaged;

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

        if (!kvpIsUnmanaged)
        {
            IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                w => { w.AppendLine("    Reader eleReader;"); });
            writer.AppendLine();
        }

        if (isReadOnlyDictionary)
        {
            // ReadOnlyDictionary requires a dictionary in its constructor
            writer.Append("    var tempDict = new System.Collections.Generic.Dictionary<");
            writer.Append(keyType.DisplayName);
            writer.Append(", ");
            writer.Append(valueType.DisplayName);
            writer.AppendLine(">(length);");
            writer.AppendLine("    for (int i = 0; i < length; i++)");
            writer.AppendLine("    {");
        }
        else
        {
            writer.Append("    value = new ");
            writer.Append(concreteTypeName);
            writer.Append(concreteTypeName.StartsWith("System.Collections.Generic.Dictionary") ? "(length)" : "()");
            writer.AppendLine(";");
            writer.AppendLine("    for (int i = 0; i < length; i++)");
            writer.AppendLine("    {");
        }

        if (kvpIsUnmanaged)
        {
            // For unmanaged KVP, use UnsafeRead directly (no WeakVersionTolerance needed)
            writer.Append("        reader.UnsafeRead(out ");
            writer.Append(kvpTypeName);
            writer.AppendLine(" kvp);");

            if (isReadOnlyDictionary)
            {
                writer.AppendLine("        tempDict[kvp.Key] = kvp.Value;");
            }
            else
            {
                writer.AppendLine("        value[kvp.Key] = kvp.Value;");
            }
        }
        else
        {
            // For managed KVP, deserialize Key and Value separately with WeakVersionTolerance
            IfElseDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                w =>
                {
                    w.AppendLine("        eleReader = reader.Slice();");
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(keyType, "key", isOutVariable: true, readerName: "eleReader"));
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(valueType, "val", isOutVariable: true, readerName: "eleReader"));
                },
                w =>
                {
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(keyType, "key", isOutVariable: true));
                    w.Append("        ");
                    w.AppendLine(GetDeserializeString(valueType, "val", isOutVariable: true));
                });

            if (isReadOnlyDictionary)
            {
                writer.AppendLine("        tempDict[key] = val;");
            }
            else
            {
                writer.AppendLine("        value[key] = val;");
            }
        }

        writer.AppendLine("    }");

        if (isReadOnlyDictionary)
        {
            writer.Append("    value = new ");
            writer.Append(typeName);
            writer.AppendLine("(tempDict);");
        }

        writer.AppendLine("}");
        writer.AppendLine();

        // Ref overload
        WriteAggressiveInlining(writer);
        writer.Append("public static void DeserializeRef(ref ");
        writer.Append(typeName);
        writer.AppendLine(" value, ref Reader reader)");
        writer.AppendLine("{");

        if (isIDictionary || isReadOnlyDictionary)
        {
            // For interfaces and ReadOnlyDictionary (immutable), just call the out overload
            writer.AppendLine("    Deserializer.Deserialize(out value, ref reader);");
        }
        else
        {
            // For concrete types - dictionaries are modifiable, clear and repopulate
            EofCheck(writer);

            writer.AppendLine();
            writer.AppendLine("    if (!reader.ReadCollectionHeader(out var length))");
            writer.AppendLine("    {");
            writer.AppendLine("        value = null;");
            writer.AppendLine("        return;");
            writer.AppendLine("    }");
            writer.AppendLine();

            if (!kvpIsUnmanaged)
            {
                IfDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                    w => { w.AppendLine("    Reader eleReader;"); });
                writer.AppendLine();
            }

            writer.AppendLine("    // Initialize if null, otherwise clear");
            writer.AppendLine("    if (value == null)");
            writer.AppendLine("    {");
            writer.Append("        value = new ");
            writer.Append(concreteTypeName);
            writer.Append(concreteTypeName.StartsWith("System.Collections.Generic.Dictionary") ? "(length)" : "()");
            writer.AppendLine(";");
            writer.AppendLine("    }");
            writer.AppendLine("    else");
            writer.AppendLine("    {");
            writer.AppendLine("        value.Clear();");
            writer.AppendLine("    }");
            writer.AppendLine();
            writer.AppendLine("    for (int i = 0; i < length; i++)");
            writer.AppendLine("    {");

            if (kvpIsUnmanaged)
            {
                // For unmanaged KVP, use UnsafeRead directly (no WeakVersionTolerance needed)
                writer.Append("        reader.UnsafeRead(out ");
                writer.Append(kvpTypeName);
                writer.AppendLine(" kvp);");
                writer.AppendLine("        value[kvp.Key] = kvp.Value;");
            }
            else
            {
                // For managed KVP, deserialize Key and Value separately with WeakVersionTolerance
                IfElseDirective(NinoConstants.WeakVersionToleranceSymbol, writer,
                    w =>
                    {
                        w.AppendLine("        eleReader = reader.Slice();");
                        w.Append("        ");
                        w.AppendLine(GetDeserializeString(keyType, "key", isOutVariable: true, readerName: "eleReader"));
                        w.Append("        ");
                        w.AppendLine(GetDeserializeString(valueType, "val", isOutVariable: true, readerName: "eleReader"));
                    },
                    w =>
                    {
                        w.Append("        ");
                        w.AppendLine(GetDeserializeString(keyType, "key", isOutVariable: true));
                        w.Append("        ");
                        w.AppendLine(GetDeserializeString(valueType, "val", isOutVariable: true));
                    });
                writer.AppendLine("        value[key] = val;");
            }

            writer.AppendLine("    }");
        }

        writer.AppendLine("}");
    }
}
