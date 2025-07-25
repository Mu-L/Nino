using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Nino.Generator.Collection;
using Nino.Generator.Metadata;
using Nino.Generator.Template;

namespace Nino.Generator.Common;

public partial class DeserializerGenerator(
    Compilation compilation,
    NinoGraph ninoGraph,
    List<NinoType> ninoTypes,
    List<ITypeSymbol> potentialTypes)
    : NinoCommonGenerator(compilation, ninoGraph, ninoTypes)
{
    private void GenerateGenericRegister(StringBuilder sb, string name, HashSet<ITypeSymbol> generatedTypes,
        HashSet<ITypeSymbol> registeredTypes)
    {
        sb.AppendLine($$"""
                                private static void Register{{name}}Deserializers()
                                {
                        """);
        // Order types: top types first, then types with base types, then others
        var orderedTypes = generatedTypes
            .Where(t => !t.IsRefStruct())
            .ToList(); // Convert to list first to avoid ordering issues
        
        // Manual ordering instead of OrderBy to avoid IComparable issues
        var topTypes = orderedTypes.Where(t => 
            NinoGraph.TopTypes.Any(topType => SymbolEqualityComparer.Default.Equals(topType.TypeSymbol, t))).ToList();
        var typeMapTypes = orderedTypes.Where(t => 
            NinoGraph.TypeMap.ContainsKey(t.GetDisplayString()) && 
            !NinoGraph.TopTypes.Any(topType => SymbolEqualityComparer.Default.Equals(topType.TypeSymbol, t))).ToList();
        var otherTypes = orderedTypes.Where(t => 
            !NinoGraph.TypeMap.ContainsKey(t.GetDisplayString()) && 
            !NinoGraph.TopTypes.Any(topType => SymbolEqualityComparer.Default.Equals(topType.TypeSymbol, t))).ToList();

        foreach (var type in topTypes.Concat(typeMapTypes).Concat(otherTypes))
        {
            var typeFullName = type.GetDisplayString();
            if (NinoGraph.TypeMap.TryGetValue(type.GetDisplayString(), out var ninoType))
            {
                var baseTypes = NinoGraph.BaseTypes[ninoType];
                string prefix;
                foreach (var baseType in baseTypes)
                {
                    if (registeredTypes.Add(baseType.TypeSymbol))
                    {
                        var baseTypeName = baseType.TypeSymbol.GetDisplayString();
                        prefix = !string.IsNullOrEmpty(baseType.CustomSerializer)
                            ? $"{baseType.CustomSerializer}."
                            : "";
                        var method = baseType.TypeSymbol.IsInstanceType() ? $"{prefix}DeserializeImpl" : "null";
                        sb.AppendLine($$"""
                                                    NinoTypeMetadata.RegisterDeserializer<{{baseTypeName}}>({{method}}, {{baseType.Parents.Any().ToString().ToLower()}});
                                        """);
                    }
                }

                prefix = !string.IsNullOrEmpty(ninoType.CustomDeserializer) ? $"{ninoType.CustomDeserializer}." : "";
                if (registeredTypes.Add(type))
                {
                    var method = ninoType.TypeSymbol.IsInstanceType() ? $"{prefix}DeserializeImpl" : "null";
                    sb.AppendLine($$"""
                                                NinoTypeMetadata.RegisterDeserializer<{{typeFullName}}>({{method}}, {{ninoType.Parents.Any().ToString().ToLower()}});
                                    """);
                }

                var meth = ninoType.TypeSymbol.IsInstanceType() ? $"{prefix}DeserializeImpl" : "null";
                foreach (var baseType in baseTypes)
                {
                    var baseTypeName = baseType.TypeSymbol.GetDisplayString();
                    sb.AppendLine($$"""
                                                NinoTypeMetadata.RecordSubTypeDeserializer<{{baseTypeName}}, {{typeFullName}}>({{meth}});
                                    """);
                }

                continue;
            }

            if (registeredTypes.Add(type))
                sb.AppendLine($$"""
                                            NinoTypeMetadata.RegisterDeserializer<{{typeFullName}}>(Deserialize, false);
                                """);
        }

        sb.AppendLine("        }");
    }

    protected override void Generate(SourceProductionContext spc)
    {
        var compilation = Compilation;
        HashSet<ITypeSymbol> registeredTypes = new(SymbolEqualityComparer.Default);

        StringBuilder sb = new(32_000_000);
        HashSet<ITypeSymbol> collectionTypes = new(SymbolEqualityComparer.Default);
        new CollectionDeserializerGenerator(compilation, potentialTypes, NinoGraph).Generate(spc, collectionTypes);
        GenerateGenericRegister(sb, "Collection", collectionTypes, registeredTypes);

        HashSet<ITypeSymbol> trivialTypes = new(SymbolEqualityComparer.Default);
        // add string type
        trivialTypes.Add(compilation.GetSpecialType(SpecialType.System_String));
        GenerateTrivialCode(spc, collectionTypes, trivialTypes);
        GenerateGenericRegister(sb, "Trivial", trivialTypes, registeredTypes);

        var curNamespace = compilation.AssemblyName!.GetNamespace();
        // generate code
        var genericCode = $$"""
                            // <auto-generated/>
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
                                            RegisterCollectionDeserializers();
                                            _initialized = true;
                                        }
                                    }
                                    
                                #if UNITY_2020_2_OR_NEWER
                                #if UNITY_EDITOR
                                    [UnityEditor.InitializeOnLoadMethod]
                                    private static void InitEditor() => Init();
                                #endif
                                
                                    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
                                    private static void InitRuntime() => Init();
                                #endif
                                
                            {{sb}}    }
                            }
                            """;

        spc.AddSource($"{curNamespace}.Deserializer.Generic.g.cs", genericCode);
    }
}