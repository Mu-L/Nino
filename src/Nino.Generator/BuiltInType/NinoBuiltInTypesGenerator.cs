// NinoBuiltInTypesGenerator.cs
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
using System.Text;
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;
using Nino.Generator.Template;

namespace Nino.Generator.BuiltInType;

/// <summary>
/// Unified generator that consolidates all built-in type generators into a single set of output files.
/// This reduces file count from 3*N to 3 files total (Serializer, Deserializer, Registration).
/// </summary>
public class NinoBuiltInTypesGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    NinoGraph ninoGraph,
    HashSet<int> potentialTypeIds,
    HashSet<int> selectedTypeIds,
    bool isUnityAssembly) : NinoGenerator(typeInfoCache, assemblyNamespace, isUnityAssembly)
{
    private readonly NinoBuiltInTypeGenerator[] _generators =
    {
        new NullableGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new KeyValuePairGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new TupleGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new ArrayGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new DictionaryGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new ListGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new ArraySegmentGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new QueueGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new StackGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new HashSetGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new LinkedListGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new ImmutableArrayGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new ImmutableListGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new PriorityQueueGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
        new SortedSetGenerator(typeInfoCache, assemblyNamespace, ninoGraph, potentialTypeIds, selectedTypeIds, isUnityAssembly),
    };

    protected override void Generate(SourceProductionContext spc)
    {
        // Clear and pre-filter to identify which types are handled by built-in generators
        selectedTypeIds.Clear();
        var filterTypeIds = potentialTypeIds.ToList()
            .OrderBy(typeId =>
            {
                if (!typeInfoCache.TryGetValue(typeId, out var typeInfo)) return int.MaxValue;
                // For built-in types, hierarchy level is not critical for ordering
                // Use empty parent list for ordering purposes
                return typeInfo.GetTypeHierarchyLevel(EquatableArray<int>.Empty);
            })
            .ToList();

        foreach (var typeId in filterTypeIds)
        {
            if (!typeInfoCache.TryGetValue(typeId, out var typeInfo)) continue;

            foreach (var generator in _generators)
            {
                if (generator.Filter(typeInfo))
                {
                    selectedTypeIds.Add(typeId);
                    break;
                }
            }
        }

        NinoBuiltInTypeGenerator.Writer serializerWriter = new("        ");
        serializerWriter.AppendLine();
        NinoBuiltInTypeGenerator.Writer deserializerWriter = new("        ");
        deserializerWriter.AppendLine();
        HashSet<int> registeredTypeIds = new();

        // Process each generator and collect their outputs
        foreach (var generator in _generators)
        {
            var (serializerCode, deserializerCode, typeIds) = generator.GenerateCode(potentialTypeIds);

            if (typeIds.Count > 0)
            {
                serializerWriter.Append(serializerCode);
                serializerWriter.AppendLine();
                deserializerWriter.Append(deserializerCode);
                deserializerWriter.AppendLine();

                foreach (var typeId in typeIds)
                {
                    registeredTypeIds.Add(typeId);
                }
            }
        }

        // Generate registration code
        StringBuilder registrationCode = new();
        foreach (var typeId in registeredTypeIds)
        {
            if (!typeInfoCache.TryGetValue(typeId, out var typeInfo)) continue;

            var typeName = typeInfo.DisplayName;
            registrationCode.AppendLine(
                $"                NinoTypeMetadata.RegisterSerializer<{typeName}>(Serializer.Serialize, false);");
            registrationCode.AppendLine(
                $"                NinoTypeMetadata.RegisterDeserializer<{typeName}>(-1, Deserializer.Deserialize, Deserializer.DeserializeRef, Deserializer.Deserialize, Deserializer.DeserializeRef, false);");
        }

        var curNamespace = AssemblyNamespace;

        // Generate serializer file
        var code = $$"""
                     // <auto-generated/>
                     #nullable disable
                     #pragma warning disable CS8669

                     using System;
                     using global::Nino.Core;
                     using System.Buffers;
                     using System.Collections.Generic;
                     using System.Collections.Concurrent;
                     using System.Runtime.InteropServices;
                     using System.Runtime.CompilerServices;

                     namespace {{curNamespace}}
                     {
                         public static partial class Serializer
                         {
                     {{serializerWriter}}    }
                     }
                     """;

        spc.AddSource($"{curNamespace}.NinoBuiltInTypes.Serializer.g.cs", code);

        // Generate deserializer file
        code = $$"""
                 // <auto-generated/>
                 #nullable disable
                 #pragma warning disable CS8669

                 using System;
                 using global::Nino.Core;
                 using System.Buffers;
                 using System.Collections.Generic;
                 using System.Collections.Concurrent;
                 using System.Runtime.InteropServices;
                 using System.Runtime.CompilerServices;

                 namespace {{curNamespace}}
                 {
                     public static partial class Deserializer
                     {
                 {{deserializerWriter}}    }
                 }
                 """;
        spc.AddSource($"{curNamespace}.NinoBuiltInTypes.Deserializer.g.cs", code);

        // Generate registration file
        if (registrationCode.Length > 0)
        {
            // Unity initialization code (conditional)
            var unityInitCode = IsUnityAssembly ? """

                     #if UNITY_2020_2_OR_NEWER
                     #if UNITY_EDITOR
                             [UnityEditor.InitializeOnLoadMethod]
                             private static void InitEditor() => Init();
                     #endif

                             [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
                             private static void InitRuntime() => Init();
                     #endif
                     """ : string.Empty;

            code = $$"""
                     // <auto-generated/>
                     #pragma warning disable CS8669

                     using System;
                     using global::Nino.Core;
                     using System.Collections.Concurrent;
                     using System.Runtime.InteropServices;
                     using System.Runtime.CompilerServices;

                     namespace {{curNamespace}}
                     {
                         public static class NinoBuiltInTypesRegistration
                         {
                             private static bool _initialized;
                             private static object _lock = new object();

                             static NinoBuiltInTypesRegistration()
                             {
                                 Init();
                             }

                             #if NET5_0_OR_GREATER
                             [ModuleInitializer]
                             #endif
                             public static void Init()
                             {
                                 lock (_lock)
                                 {
                                     if (_initialized)
                                         return;

                     {{registrationCode}}
                                     _initialized = true;
                                 }
                             }
                     {{unityInitCode}}
                         }
                     }
                     """;
            spc.AddSource($"{curNamespace}.NinoBuiltInTypes.Registration.g.cs", code);
        }
    }
}