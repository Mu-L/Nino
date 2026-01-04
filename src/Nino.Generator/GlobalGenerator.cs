using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nino.Generator.BuiltInType;
using Nino.Generator.Common;
using Nino.Generator.Metadata;
using Nino.Generator.Pipeline;
using Nino.Generator.Template;

namespace Nino.Generator;

[Generator(LanguageNames.CSharp)]
public class GlobalGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Initialize the incremental source generator pipeline.
    /// Uses multi-stage incremental pipeline with pure DTO transformations for optimal caching.
    /// </summary>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ===================================================================
        // STAGE 1: Extract Compilation Metadata (value-based for caching)
        // ===================================================================
        var compilationMetadata = context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                var isUnityAssembly = compilation.ReferencedAssemblyNames.Any(a =>
                    a.Name == "UnityEngine" ||
                    a.Name == "UnityEngine.CoreModule" ||
                    a.Name == "UnityEditor");

                var hasNinoCoreUsage = compilation.SyntaxTrees.Any(tree =>
                    tree.GetRoot().DescendantNodes()
                        .OfType<UsingDirectiveSyntax>()
                        .Any(u => u.Name.ToString().Contains("Nino.Core")));

                var assemblyName = compilation.AssemblyName ?? "Unknown";
                var assemblyNamespace = assemblyName.GetNamespace();

                return new CompilationMetadata(
                    AssemblyName: assemblyName,
                    IsUnityAssembly: isUnityAssembly,
                    HasNinoCoreUsage: hasNinoCoreUsage,
                    AssemblyNamespace: assemblyNamespace
                );
            });

        // ===================================================================
        // STAGE 2: Extract NinoType DTOs from [NinoType] attributed types
        // ===================================================================
        var directNinoTypes = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Nino.Core.NinoTypeAttribute",
                predicate: static (s, _) => s is TypeDeclarationSyntax,
                transform: static (ctx, ct) =>
                {
                    if (ctx.TargetSymbol is not ITypeSymbol typeSymbol)
                        return (NinoType?)null;

                    // Only process public types
                    if (typeSymbol.DeclaredAccessibility != Accessibility.Public)
                        return (NinoType?)null;

                    // Skip invalid generic types
                    if (!typeSymbol.CheckGenericValidity())
                        return (NinoType?)null;

                    // Extract complete type information into DTO
                    var typeInfo = SymbolDataExtractor.ExtractTypeInfo(typeSymbol, ct);

                    // Get NinoType attribute data
                    var ninoAttr = SymbolDataExtractor.GetNinoTypeAttribute(typeSymbol);

                    // Extract members
                    var members = SymbolDataExtractor.ExtractMembers(
                        typeSymbol,
                        ninoAttr?.ContainNonPublicMembers ?? false,
                        ct);

                    // Extract parent type IDs (base class + interfaces)
                    var parentIds = SymbolDataExtractor.ExtractParentTypeIds(typeSymbol, ct);

                    // Extract custom serializers from referenced assemblies
                    var compilation = ctx.SemanticModel.Compilation;
                    var (customSer, customDeser) =
                        SymbolDataExtractor.ExtractCustomSerializers(
                            typeSymbol,
                            compilation.Assembly);

                    // Extract RefDeserializationMethod
                    var refDeser = SymbolDataExtractor.ExtractRefDeserializationMethod(typeSymbol);

                    // Extract constructors for deserialization
                    var constructors = SymbolDataExtractor.ExtractConstructors(typeSymbol, ct);

                    // Build NinoType DTO
                    // NOTE: IsPolymorphic, IsCircular, HierarchyLevel will be computed in graph building phase
                    return new NinoType
                    {
                        TypeInfo = typeInfo,
                        Members = members,
                        ParentTypeIds = parentIds,
                        CustomSerializer = customSer,
                        CustomDeserializer = customDeser,
                        RefDeserializationMethod = refDeser,
                        Constructors = constructors,
                        IsPolymorphic = false, // computed in graph phase
                        IsCircular = false,    // computed in graph phase
                        HierarchyLevel = 0     // computed in graph phase
                    };
                })
            .Where(static nt => nt != null)
            .Collect();

        // ===================================================================
        // STAGE 3: Extract NinoType DTOs from inherited types
        // ===================================================================
        var inheritedNinoTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is TypeDeclarationSyntax tds &&
                    (tds.AttributeLists.Count > 0 || tds.BaseList != null),
                transform: static (ctx, ct) =>
                {
                    if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not ITypeSymbol typeSymbol)
                        return (NinoType?)null;

                    // Only process public types
                    if (typeSymbol.DeclaredAccessibility != Accessibility.Public)
                        return (NinoType?)null;

                    // Skip invalid generic types
                    if (!typeSymbol.CheckGenericValidity())
                        return (NinoType?)null;

                    // Check if this type inherits NinoTypeAttribute
                    if (!SymbolDataExtractor.IsNinoType(typeSymbol))
                        return (NinoType?)null;

                    // Extract complete type information into DTO
                    var typeInfo = SymbolDataExtractor.ExtractTypeInfo(typeSymbol, ct);

                    // Get NinoType attribute data (may be inherited)
                    var ninoAttr = SymbolDataExtractor.GetNinoTypeAttribute(typeSymbol);

                    // Extract members
                    var members = SymbolDataExtractor.ExtractMembers(
                        typeSymbol,
                        ninoAttr?.ContainNonPublicMembers ?? false,
                        ct);

                    // Extract parent type IDs
                    var parentIds = SymbolDataExtractor.ExtractParentTypeIds(typeSymbol, ct);

                    // Extract custom serializers
                    var compilation = ctx.SemanticModel.Compilation;
                    var (customSer, customDeser) =
                        SymbolDataExtractor.ExtractCustomSerializers(
                            typeSymbol,
                            compilation.Assembly);

                    // Extract RefDeserializationMethod
                    var refDeser = SymbolDataExtractor.ExtractRefDeserializationMethod(typeSymbol);

                    // Extract constructors for deserialization
                    var constructors = SymbolDataExtractor.ExtractConstructors(typeSymbol, ct);

                    // Build NinoType DTO
                    return new NinoType
                    {
                        TypeInfo = typeInfo,
                        Members = members,
                        ParentTypeIds = parentIds,
                        CustomSerializer = customSer,
                        CustomDeserializer = customDeser,
                        RefDeserializationMethod = refDeser,
                        Constructors = constructors,
                        IsPolymorphic = false, // computed in graph phase
                        IsCircular = false,    // computed in graph phase
                        HierarchyLevel = 0     // computed in graph phase
                    };
                })
            .Where(static nt => nt != null)
            .Collect();

        // ===================================================================
        // STAGE 4: Merge and Deduplicate NinoTypes by TypeId
        // ===================================================================
        var allNinoTypes = directNinoTypes
            .Combine(inheritedNinoTypes)
            .Select(static (combined, _) =>
            {
                var (direct, inherited) = combined;

                // Use TypeId-based dictionary for deduplication
                var uniqueTypes = new Dictionary<int, NinoType>();

                // Add directly attributed types first (prefer these)
                foreach (var ninoType in direct)
                {
                    if (ninoType.HasValue)
                    {
                        var nt = ninoType.Value;
                        uniqueTypes[nt.TypeInfo.TypeId] = nt;
                    }
                }

                // Add inherited types if not already present
                foreach (var ninoType in inherited)
                {
                    if (ninoType.HasValue)
                    {
                        var nt = ninoType.Value;
                        if (!uniqueTypes.ContainsKey(nt.TypeInfo.TypeId))
                        {
                            uniqueTypes[nt.TypeInfo.TypeId] = nt;
                        }
                    }
                }

                return new EquatableArray<NinoType>(uniqueTypes.Values.ToArray());
            });

        // ===================================================================
        // STAGE 5: Build Type Hierarchy Graph from Pure DTOs
        // ===================================================================
        var graphProvider = allNinoTypes
            .Select(static (ninoTypes, ct) =>
            {
                // Build complete type hierarchy graph using pure DTO logic
                // This replaces the old NinoGraph constructor
                return NinoGraphBuilder.Build(ninoTypes, ct);
            });

        // ===================================================================
        // STAGE 6: Combine Metadata and Graph for Code Generation
        // ===================================================================
        var finalProvider = compilationMetadata.Combine(graphProvider);

        // ===================================================================
        // STAGE 7: Generate Code (Pure DTOs - No ITypeSymbol!)
        // ===================================================================
        context.RegisterSourceOutput(finalProvider,
            (spc, source) =>
            {
                CompilationMetadata metadata = source.Left;
                NinoGraph graph = source.Right;

                // Early exit if compilation doesn't reference Nino.Core
                if (!metadata.HasNinoCoreUsage)
                    return;

                try
                {
                    // Add debug output showing graph structure
                    spc.AddSource($"{metadata.AssemblyNamespace}.Graph.g.cs",
                        $"/*\n{graph}\n*/");

                    var ninoTypes = graph.TypeMap.Values.ToList();
                    spc.AddSource($"{metadata.AssemblyNamespace}.Types.g.cs",
                        $"/*\n{string.Join("\n", ninoTypes.Where(t => t.Members.Length > 0))}\n*/");

                    // Execute all generators with pure DTOs
                    ExecuteGenerators(spc, metadata, graph);
                }
                catch (Exception ex)
                {
                    // Report but don't fail completely
                    spc.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("NINO998",
                            "Nino Generator Warning",
                            $"Generator encountered issue but continuing: {ex.Message}",
                            "Nino.Generator",
                            DiagnosticSeverity.Warning,
                            true,
                            description: $"Stack trace: {ex.StackTrace}"),
                        Location.None));
                }
            });
    }

    /// <summary>
    /// Execute all code generators using only pure DTOs.
    /// NO ITypeSymbol or Compilation references allowed here!
    /// </summary>
    private static void ExecuteGenerators(
        SourceProductionContext spc,
        CompilationMetadata metadata,
        NinoGraph graph)
    {
        // Build type info cache for quick lookups
        var typeInfoCache = new Dictionary<int, TypeInfoDto>();
        foreach (var ninoType in graph.TypeMap.Values)
        {
            typeInfoCache[ninoType.TypeInfo.TypeId] = ninoType.TypeInfo;
        }

        var ninoTypes = graph.TypeMap.Values.ToList();
        var assemblyNamespace = metadata.AssemblyNamespace;
        var isUnityAssembly = metadata.IsUnityAssembly;

        try
        {
            // Phase 5: All generators now use DTO-based API
            var ninoTypesArray = new EquatableArray<NinoType>(ninoTypes.ToArray());

            // Build generated type IDs for built-in types
            // Extract all TypeIds from ninoTypes and their referenced types
            var generatedBuiltInTypeIds = new HashSet<int>();
            foreach (var ninoType in ninoTypes)
            {
                // Collect TypeIds from all members
                foreach (var member in ninoType.Members)
                {
                    if (typeInfoCache.ContainsKey(member.Type.TypeId))
                    {
                        generatedBuiltInTypeIds.Add(member.Type.TypeId);
                    }
                }
            }

            // 1. Type Constants Generator
            ExecuteGeneratorSafe(
                spc,
                "TypeConstGenerator",
                () =>
                {
                    var gen = new TypeConstGenerator(
                        typeInfoCache,
                        assemblyNamespace,
                        graph,
                        ninoTypesArray);
                    gen.Execute(spc);
                });

            // 2. Unsafe Accessor Generator
            ExecuteGeneratorSafe(
                spc,
                "UnsafeAccessorGenerator",
                () =>
                {
                    var gen = new UnsafeAccessorGenerator(
                        typeInfoCache,
                        assemblyNamespace,
                        graph,
                        ninoTypesArray);
                    gen.Execute(spc);
                });

            // 3. Partial Class Generator
            ExecuteGeneratorSafe(
                spc,
                "PartialClassGenerator",
                () =>
                {
                    var gen = new PartialClassGenerator(
                        typeInfoCache,
                        assemblyNamespace,
                        graph,
                        ninoTypesArray);
                    gen.Execute(spc);
                });

            // 4. Serializer Generator
            ExecuteGeneratorSafe(
                spc,
                "SerializerGenerator",
                () =>
                {
                    var gen = new SerializerGenerator(
                        typeInfoCache,
                        assemblyNamespace,
                        graph,
                        ninoTypesArray,
                        generatedBuiltInTypeIds,
                        isUnityAssembly);
                    gen.Execute(spc);
                });

            // 5. Deserializer Generator
            ExecuteGeneratorSafe(
                spc,
                "DeserializerGenerator",
                () =>
                {
                    var gen = new DeserializerGenerator(
                        typeInfoCache,
                        assemblyNamespace,
                        graph,
                        ninoTypesArray,
                        generatedBuiltInTypeIds,
                        isUnityAssembly);
                    gen.Execute(spc);
                });

            // 6. Built-In Types Generator
            ExecuteGeneratorSafe(
                spc,
                "NinoBuiltInTypesGenerator",
                () =>
                {
                    // Potential types are all types in the cache
                    var potentialTypeIds = new HashSet<int>(typeInfoCache.Keys);
                    var selectedTypeIds = new HashSet<int>();

                    var gen = new NinoBuiltInTypesGenerator(
                        typeInfoCache,
                        assemblyNamespace,
                        graph,
                        potentialTypeIds,
                        selectedTypeIds,
                        isUnityAssembly);
                    gen.Execute(spc);
                });
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("NINO999",
                    "Code Generation Failed",
                    $"Failed to execute generators: {ex.Message}",
                    "Nino.Generator",
                    DiagnosticSeverity.Error,
                    true,
                    description: $"Stack trace: {ex.StackTrace}"),
                Location.None));
        }
    }

    /// <summary>
    /// Execute a single generator with error handling.
    /// </summary>
    private static void ExecuteGeneratorSafe(
        SourceProductionContext spc,
        string generatorName,
        Action executeAction)
    {
        try
        {
            executeAction();
        }
        catch (Exception ex)
        {
            // Report specific generator failure with details
            spc.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("NINO997",
                    $"{generatorName} Error",
                    $"{generatorName} failed: {ex.GetType().Name} - {ex.Message}",
                    "Nino.Generator",
                    DiagnosticSeverity.Warning,
                    true,
                    description: $"Stack trace: {ex.StackTrace}"),
                Location.None));

            // Add comment in generated code for debugging
            spc.AddSource($"{generatorName}.Error.g.cs",
                $@"/*
{generatorName} failed to generate code.
Error: {ex.GetType().Name}: {ex.Message}

Stack Trace:
{ex.StackTrace}

This error has been logged as a warning.
Generators will be updated in Phase 5 to use new DTO-based API.
*/");
        }
    }
}
