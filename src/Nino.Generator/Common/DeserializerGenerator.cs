using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;
using Nino.Generator.Template;

namespace Nino.Generator.Common;

public partial class DeserializerGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    NinoGraph ninoGraph,
    EquatableArray<NinoType> ninoTypes,
    HashSet<int> generatedBuiltInTypeIds,
    bool isUnityAssembly)
    : NinoCommonGenerator(typeInfoCache, assemblyNamespace, ninoGraph, ninoTypes, isUnityAssembly)
{
    protected readonly HashSet<int> GeneratedBuiltInTypeIds = generatedBuiltInTypeIds;

    private void GenerateGenericRegister(StringBuilder sb, string name, HashSet<int> generatedTypeIds,
        HashSet<int> registeredTypeIds)
    {
        sb.AppendLine($$"""
                                private static void Register{{name}}Deserializers()
                                {
                        """);
        // Order types: top types first, then types with base types, then others
        var orderedTypeIds = generatedTypeIds
            .Where(typeId => TypeInfoCache.TryGetValue(typeId, out var typeInfo) && !typeInfo.IsRefLikeType)
            .ToList();

        // Manual ordering instead of OrderBy to avoid IComparable issues
        var topTypeIds = orderedTypeIds.Where(typeId =>
            NinoGraph.TopTypes.Any(topType => topType.TypeInfo.TypeId == typeId)).ToList();
        var typeMapTypeIds = orderedTypeIds.Where(typeId =>
        {
            if (!TypeInfoCache.TryGetValue(typeId, out var typeInfo)) return false;
            return NinoGraph.TypeMap.ContainsKey(typeInfo.DisplayName) &&
                   !NinoGraph.TopTypes.Any(topType => topType.TypeInfo.TypeId == typeId);
        }).ToList();
        var otherTypeIds = orderedTypeIds.Where(typeId =>
        {
            if (!TypeInfoCache.TryGetValue(typeId, out var typeInfo)) return false;
            return !NinoGraph.TypeMap.ContainsKey(typeInfo.DisplayName) &&
                   !NinoGraph.TopTypes.Any(topType => topType.TypeInfo.TypeId == typeId);
        }).ToList();

        foreach (var typeId in topTypeIds.Concat(typeMapTypeIds).Concat(otherTypeIds))
        {
            if (!TypeInfoCache.TryGetValue(typeId, out var typeInfo)) continue;
            var typeFullName = typeInfo.DisplayName;

            if (NinoGraph.TypeMap.TryGetValue(typeInfo.DisplayName, out var ninoType))
            {
                // Use TryGetValue to avoid KeyNotFoundException during concurrent execution
                var baseTypes = NinoGraph.BaseTypes.TryGetValue(ninoType, out var bases)
                    ? bases
                    : new List<NinoType>();

                string prefix;
                foreach (var baseType in baseTypes)
                {
                    if (registeredTypeIds.Add(baseType.TypeInfo.TypeId))
                    {
                        var baseTypeName = baseType.TypeInfo.DisplayName;
                        prefix = !string.IsNullOrEmpty(baseType.CustomDeserializer)
                            ? $"{baseType.CustomDeserializer}."
                            : "";
                        var method = TypeInfoDtoExtensions.IsInstanceType(baseType.TypeInfo) ? $"{prefix}DeserializeImpl" : "null";
                        var methodRef = TypeInfoDtoExtensions.IsInstanceType(baseType.TypeInfo) ? $"{prefix}DeserializeImplRef" : "null";
                        var baseTypeId = !baseType.IsPolymorphic || !TypeInfoDtoExtensions.IsInstanceType(baseType.TypeInfo)
                            ? "-1"
                            : $"NinoTypeConst.{baseType.TypeInfo.DisplayName.GetTypeConstName()}";

                        // For polymorphic base types, use the polymorphic methods as optimal deserializers
                        var optimalMethod = method;
                        var optimalMethodRef = methodRef;
                        if (!TypeInfoDtoExtensions.IsSealedOrStruct(baseType.TypeInfo))
                        {
                            optimalMethod = $"{prefix}DeserializePolymorphic";
                            optimalMethodRef = $"{prefix}DeserializeRefPolymorphic";
                        }

                        sb.AppendLine($$"""
                                                    NinoTypeMetadata.RegisterDeserializer<{{baseTypeName}}>({{baseTypeId}}, {{method}}, {{methodRef}}, {{optimalMethod}}, {{optimalMethodRef}}, {{(baseType.ParentTypeIds.Length > 0).ToString().ToLower()}});
                                        """);
                    }
                }

                var currentTypeId = !ninoType.IsPolymorphic || !TypeInfoDtoExtensions.IsInstanceType(typeInfo)
                    ? "-1"
                    : $"NinoTypeConst.{typeInfo.DisplayName.GetTypeConstName()}";
                prefix = !string.IsNullOrEmpty(ninoType.CustomDeserializer) ? $"{ninoType.CustomDeserializer}." : "";
                if (registeredTypeIds.Add(typeId))
                {
                    var method = TypeInfoDtoExtensions.IsInstanceType(ninoType.TypeInfo) ? $"{prefix}DeserializeImpl" : "null";
                    var methodRef = TypeInfoDtoExtensions.IsInstanceType(ninoType.TypeInfo) ? $"{prefix}DeserializeImplRef" : "null";

                    // For polymorphic types, use the polymorphic methods as optimal deserializers
                    var optimalMethod = method;
                    var optimalMethodRef = methodRef;
                    if (!TypeInfoDtoExtensions.IsSealedOrStruct(ninoType.TypeInfo))
                    {
                        optimalMethod = $"{prefix}DeserializePolymorphic";
                        optimalMethodRef = $"{prefix}DeserializeRefPolymorphic";
                    }

                    sb.AppendLine($$"""
                                                NinoTypeMetadata.RegisterDeserializer<{{typeFullName}}>({{currentTypeId}}, {{method}}, {{methodRef}}, {{optimalMethod}}, {{optimalMethodRef}}, {{(ninoType.ParentTypeIds.Length > 0).ToString().ToLower()}});
                                    """);
                }

                var meth = TypeInfoDtoExtensions.IsInstanceType(ninoType.TypeInfo) ? $"{prefix}DeserializeImpl" : "null";
                var methRef = TypeInfoDtoExtensions.IsInstanceType(ninoType.TypeInfo) ? $"{prefix}DeserializeImplRef" : "null";
                foreach (var baseType in baseTypes)
                {
                    var baseTypeName = baseType.TypeInfo.DisplayName;
                    sb.AppendLine($$"""
                                                NinoTypeMetadata.RecordSubTypeDeserializer<{{baseTypeName}}, {{typeFullName}}>({{currentTypeId}},{{meth}}, {{methRef}});
                                    """);
                }

                continue;
            }

            if (registeredTypeIds.Add(typeId))
                sb.AppendLine($$"""
                                            NinoTypeMetadata.RegisterDeserializer<{{typeFullName}}>(-1, Deserialize, DeserializeRef, Deserialize, DeserializeRef, false);
                                """);
        }

        sb.AppendLine("        }");
    }

    protected override void Generate(SourceProductionContext spc)
    {
        HashSet<int> registeredTypeIds = new();

        // Reduced from 32MB to 256KB to avoid LOH allocation and memory fragmentation
        // StringBuilder will automatically grow as needed
        StringBuilder sb = new(262_144); // 256KB
        HashSet<int> trivialTypeIds = new();
        GenerateTrivialCode(spc, trivialTypeIds);
        // add string type (find in cache)
        var stringTypeId = TypeInfoCache.FirstOrDefault(kvp => kvp.Value.SpecialType == SpecialTypeDto.System_String).Key;
        if (stringTypeId != 0)
            trivialTypeIds.Add(stringTypeId);
        GenerateGenericRegister(sb, "Trivial", trivialTypeIds, registeredTypeIds);

        var curNamespace = AssemblyNamespace;

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

        // generate code
        var genericCode = $$"""
                            // <auto-generated/>
                            #nullable disable
                            #pragma warning disable CS8669
                            using System;
                            using global::Nino.Core;
                            using System.Buffers;
                            using System.ComponentModel;
                            using System.Collections.Generic;
                            using System.Collections.Concurrent;
                            using System.Runtime.InteropServices;
                            using System.Runtime.CompilerServices;

                            namespace {{curNamespace}}
                            {
                                public static partial class Deserializer
                                {
                                    private static bool _initialized;
                                    private static object _lock = new object();

                                    static Deserializer()
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

                                            RegisterTrivialDeserializers();
                                            _initialized = true;
                                        }
                                    }
                            {{unityInitCode}}

                            {{sb}}    }
                            }
                            """;

        spc.AddSource($"{curNamespace}.Deserializer.Generic.g.cs", genericCode);
    }
}