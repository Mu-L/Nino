using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;

namespace Nino.Generator.Pipeline;

/// <summary>
/// THE ONLY place where ITypeSymbol is accessed in the pipeline.
/// Extracts all required data from ITypeSymbol and converts it to pure DTOs for incremental caching.
/// All symbol-based logic is centralized here to enable proper incremental source generation.
/// </summary>
public static class SymbolDataExtractor
{
    /// <summary>
    /// Extracts complete TypeInfoDto from ITypeSymbol.
    /// This is the PRIMARY extraction point - all data needed for code generation must be extracted here.
    /// </summary>
    public static TypeInfoDto ExtractTypeInfo(ITypeSymbol typeSymbol, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Normalize and get pure type (remove nullable annotations)
        var normalizedType = typeSymbol.GetNormalizedTypeSymbol().GetPureType();

        // Extract type arguments for generic types
        var typeArguments = EquatableArray<TypeInfoDto>.Empty;
        string? genericOriginalDefinition = null;
        bool isGenericTypeDefinition = false;

        if (normalizedType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            typeArguments = new EquatableArray<TypeInfoDto>(
                namedType.TypeArguments.Select(t => ExtractTypeInfo(t, ct)));

            genericOriginalDefinition = namedType.ConstructedFrom.ToDisplayString();
            isGenericTypeDefinition = namedType.IsGenericTypeDefinition();
        }

        // Extract array information
        int arrayRank = 0;
        TypeInfoDto? arrayElementType = null;
        if (normalizedType is IArrayTypeSymbol arrayType)
        {
            arrayRank = arrayType.Rank;
            arrayElementType = ExtractTypeInfo(arrayType.ElementType, ct);
        }

        // Extract nullable information
        bool isNullableValueType = false;
        TypeInfoDto? nullableUnderlyingType = null;
        if (normalizedType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType)
        {
            isNullableValueType = true;
            if (nullableType.TypeArguments.Length > 0)
            {
                nullableUnderlyingType = ExtractTypeInfo(nullableType.TypeArguments[0], ct);
            }
        }

        // Extract tuple information
        bool isTupleType = normalizedType.IsTupleType;
        EquatableArray<TupleElementDto>? tupleElements = null;
        if (isTupleType && normalizedType is INamedTypeSymbol tupleNamedType)
        {
            var elements = tupleNamedType.TupleElements;
            if (!elements.IsDefaultOrEmpty)
            {
                tupleElements = new EquatableArray<TupleElementDto>(
                    elements.Select(e => new TupleElementDto
                    {
                        Type = ExtractTypeInfo(e.Type, ct),
                        Name = e.Name
                    }));
            }
        }

        // Pre-compute display names
        var fullyQualifiedName = normalizedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var displayName = GetDisplayString(normalizedType);
        var instanceVariableName = GetTypeInstanceName(normalizedType);
        var name = normalizedType.Name; // Simple type name (e.g., "List", "Dictionary", "ValueTuple")

        return new TypeInfoDto
        {
            // Identity
            FullyQualifiedName = fullyQualifiedName,
            AssemblyQualifiedName = GetAssemblyQualifiedName(normalizedType),
            TypeId = ComputeTypeId(normalizedType),

            // Classification
            Kind = MapTypeKind(normalizedType.TypeKind),
            SpecialType = MapSpecialType(normalizedType.SpecialType),
            Accessibility = MapAccessibility(normalizedType.DeclaredAccessibility),

            // Characteristics
            IsValueType = normalizedType.IsValueType,
            IsReferenceType = normalizedType.IsReferenceType,
            IsUnmanagedType = normalizedType.IsUnmanagedType,
            IsRefLikeType = normalizedType.IsRefLikeType,
            IsRecord = normalizedType.IsRecord,
            IsAbstract = normalizedType.IsAbstract,
            IsSealed = normalizedType.IsSealed,
            IsStatic = normalizedType.IsStatic,
            IsGenericType = normalizedType is INamedTypeSymbol { IsGenericType: true },

            // Generic info
            TypeArguments = typeArguments,
            GenericOriginalDefinition = genericOriginalDefinition,
            IsGenericTypeDefinition = isGenericTypeDefinition,

            // Array info
            ArrayRank = arrayRank,
            ArrayElementType = arrayElementType,

            // Nullable info
            IsNullableValueType = isNullableValueType,
            NullableUnderlyingType = nullableUnderlyingType,

            // Tuple info
            IsTupleType = isTupleType,
            TupleElements = tupleElements,

            // Containment
            ContainingNamespace = normalizedType.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            ContainingAssemblyName = normalizedType.ContainingAssembly?.Name ?? string.Empty,

            // Display names (pre-computed)
            DisplayName = displayName,
            InstanceVariableName = instanceVariableName,
            Name = name
        };
    }

    /// <summary>
    /// Extracts members (fields and properties) from a type symbol.
    /// </summary>
    public static EquatableArray<NinoMember> ExtractMembers(
        ITypeSymbol typeSymbol,
        bool includeNonPublic,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var members = new List<NinoMember>();
        var memberSymbols = typeSymbol.GetMembers();

        foreach (var member in memberSymbols)
        {
            // Skip if not field or property
            if (member is not (IFieldSymbol or IPropertySymbol))
                continue;

            // Skip static members
            if (member.IsStatic)
                continue;

            // Skip if non-public and not including non-public members
            if (!includeNonPublic && member.DeclaredAccessibility != Accessibility.Public)
                continue;

            // Get member type
            ITypeSymbol memberType = member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null!
            };

            if (memberType == null)
                continue;

            // Normalize and get pure type
            var normalizedType = memberType.GetNormalizedTypeSymbol().GetPureType();
            var typeInfo = ExtractTypeInfo(normalizedType, ct);

            // Check for custom formatter
            TypeInfoDto? customFormatterType = null;
            var customFormatterAttr = member.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "NinoCustomFormatterAttribute");

            if (customFormatterAttr?.ConstructorArguments.Length > 0)
            {
                var arg = customFormatterAttr.ConstructorArguments[0];
                if (arg.Value is ITypeSymbol formatterSymbol)
                {
                    customFormatterType = ExtractTypeInfo(formatterSymbol, ct);
                }
            }

            // Check for UTF-8 string attribute
            bool isUtf8String = member.GetAttributes()
                .Any(attr => attr.AttributeClass?.Name == "NinoUtf8StringAttribute");

            members.Add(new NinoMember
            {
                Name = member.Name,
                Type = typeInfo,
                IsCtorParameter = false, // Will be set by parser
                IsPrivate = member.DeclaredAccessibility != Accessibility.Public,
                IsProperty = member is IPropertySymbol,
                IsUtf8String = isUtf8String,
                IsStatic = member.IsStatic,
                IsReadOnly = member switch
                {
                    IFieldSymbol field => field.IsReadOnly,
                    IPropertySymbol prop => prop.IsReadOnly,
                    _ => false
                },
                CustomFormatterType = customFormatterType
            });
        }

        return new EquatableArray<NinoMember>(members);
    }

    /// <summary>
    /// Extracts parent type IDs (base class and interfaces).
    /// </summary>
    public static EquatableArray<int> ExtractParentTypeIds(ITypeSymbol typeSymbol, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var parentIds = new List<int>();

        // Add base type
        if (typeSymbol.BaseType != null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
        {
            var normalizedBase = typeSymbol.BaseType.GetNormalizedTypeSymbol().GetPureType();
            parentIds.Add(ComputeTypeId(normalizedBase));
        }

        // Add interfaces
        foreach (var interfaceType in typeSymbol.Interfaces)
        {
            var normalizedInterface = interfaceType.GetNormalizedTypeSymbol().GetPureType();
            parentIds.Add(ComputeTypeId(normalizedInterface));
        }

        return new EquatableArray<int>(parentIds);
    }

    /// <summary>
    /// Extracts constructor metadata for deserialization code generation.
    /// Returns all instance constructors with their parameters and attributes.
    /// </summary>
    public static EquatableArray<ConstructorInfoDto> ExtractConstructors(ITypeSymbol typeSymbol, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (typeSymbol is not INamedTypeSymbol namedType)
            return EquatableArray<ConstructorInfoDto>.Empty;

        var constructors = new List<ConstructorInfoDto>();

        foreach (var constructor in namedType.InstanceConstructors)
        {
            ct.ThrowIfCancellationRequested();

            // Skip inaccessible constructors
            if (constructor.DeclaredAccessibility == Accessibility.Private)
                continue;

            // Extract parameters
            var parameters = new List<ConstructorParameterDto>();
            foreach (var param in constructor.Parameters)
            {
                parameters.Add(new ConstructorParameterDto
                {
                    Name = param.Name,
                    Type = ExtractTypeInfo(param.Type, ct)
                });
            }

            // Check for NinoConstructorAttribute
            var ninoCtorAttr = constructor.GetNinoConstructorAttribute();
            bool hasNinoConstructor = ninoCtorAttr != null;
            EquatableArray<string>? parameterNames = null;

            if (hasNinoConstructor && ninoCtorAttr!.ConstructorArguments.Length > 0)
            {
                // Extract parameter names from [NinoConstructor(nameof(a), nameof(b), ...)]
                var args = ninoCtorAttr.ConstructorArguments[0].Values;
                var names = args.Select(a => a.Value as string)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Cast<string>()
                    .ToArray();
                parameterNames = new EquatableArray<string>(names);
            }

            // Check if this is the primary constructor (for records)
            bool isPrimary = namedType.IsRecord &&
                             constructor.Parameters.Length > 0 &&
                             constructor.Parameters.All(p => namedType.GetMembers(p.Name).Any());

            constructors.Add(new ConstructorInfoDto
            {
                Parameters = new EquatableArray<ConstructorParameterDto>(parameters),
                HasNinoConstructorAttribute = hasNinoConstructor,
                NinoConstructorParameterNames = parameterNames,
                DisplayString = constructor.ToDisplayString(),
                IsPrimaryConstructor = isPrimary,
                IsConstructor = constructor.MethodKind == MethodKind.Constructor,
                MethodName = constructor.MethodKind == MethodKind.Constructor ? null : constructor.Name
            });
        }

        return new EquatableArray<ConstructorInfoDto>(constructors);
    }

    /// <summary>
    /// Checks if a type is a NinoType (has NinoTypeAttribute or inherits from NinoType).
    /// </summary>
    public static bool IsNinoType(ITypeSymbol typeSymbol)
    {
        return GetNinoTypeAttribute(typeSymbol) != null;
    }

    /// <summary>
    /// Gets NinoTypeAttribute data from a type symbol.
    /// </summary>
    public static NinoTypeAttributeData? GetNinoTypeAttribute(ITypeSymbol typeSymbol)
    {
        // Check current type first
        var attr = GetDirectNinoTypeAttribute(typeSymbol);
        if (attr != null)
        {
            return ParseNinoTypeAttribute(attr);
        }

        // Then check base class chain
        var baseType = typeSymbol.BaseType;
        while (baseType != null)
        {
            attr = GetDirectNinoTypeAttribute(baseType);
            if (attr != null)
            {
                var ninoTypeAttr = ParseNinoTypeAttribute(attr);
                // If base class has allowInheritance = false, don't inherit
                if (ninoTypeAttr.AllowInheritance)
                {
                    return ninoTypeAttr;
                }

                // Base class doesn't allow inheritance, stop searching
                break;
            }
            baseType = baseType.BaseType;
        }

        // Finally check interfaces
        foreach (var interfaceType in typeSymbol.AllInterfaces)
        {
            attr = GetDirectNinoTypeAttribute(interfaceType);
            if (attr != null)
            {
                var ninoTypeAttr = ParseNinoTypeAttribute(attr);
                // Check allowInheritance parameter
                if (ninoTypeAttr.AllowInheritance)
                {
                    return ninoTypeAttr;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts custom serializer/deserializer information from external assembly.
    /// </summary>
    public static (string serializer, string deserializer) ExtractCustomSerializers(
        ITypeSymbol typeSymbol,
        IAssemblySymbol currentAssembly)
    {
        var declaredTypeAssembly = typeSymbol.ContainingAssembly;
        bool isSameAssembly = SymbolEqualityComparer.Default.Equals(declaredTypeAssembly, currentAssembly);

        if (isSameAssembly)
            return (string.Empty, string.Empty);

        // Check if referenced assembly has Nino generated code
        var assemblyNamespace = declaredTypeAssembly.Name.GetNamespace();

        var serializerType = declaredTypeAssembly.GetTypeByMetadataName($"{assemblyNamespace}.Serializer");
        var deserializerType = declaredTypeAssembly.GetTypeByMetadataName($"{assemblyNamespace}.Deserializer");

        return (
            serializerType?.ToDisplayString() ?? string.Empty,
            deserializerType?.ToDisplayString() ?? string.Empty
        );
    }

    /// <summary>
    /// Extracts RefDeserializationMethod from NinoRefDeserializationAttribute.
    /// Ported from CSharpParser.cs lines 357-371.
    /// Looks for a public static parameterless method that returns the same type
    /// and has the NinoRefDeserializationAttribute.
    /// </summary>
    public static string ExtractRefDeserializationMethod(ITypeSymbol typeSymbol)
    {
        // Find public static method with NinoRefDeserializationAttribute
        // Must be: public, static, no parameters, returns same type
        var refDeserMethod = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m =>
                m.DeclaredAccessibility == Accessibility.Public &&
                m.IsStatic &&
                m.Parameters.Length == 0 &&
                SymbolEqualityComparer.Default.Equals(m.ReturnType, typeSymbol) &&
                m.GetAttributes().Any(a =>
                    a.AttributeClass != null &&
                    a.AttributeClass.ToDisplayString().EndsWith("NinoRefDeserializationAttribute")));

        return refDeserMethod?.Name ?? string.Empty;
    }

    // ===== Private Helper Methods =====

    /// <summary>
    /// Computes deterministic TypeId hash from type symbol.
    /// CRITICAL: Must be deterministic across compilations for incremental caching.
    /// </summary>
    private static int ComputeTypeId(ITypeSymbol typeSymbol)
    {
        var fqn = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return GetDeterministicHashCode(fqn);
    }

    /// <summary>
    /// Deterministic hash code implementation.
    /// Reference: https://github.com/microsoft/referencesource/blob/master/mscorlib/system/string.cs
    /// </summary>
    private static int GetDeterministicHashCode(string str)
    {
        ReadOnlySpan<char> span = str.AsSpan();
        int hash1 = 5381;
        int hash2 = hash1;

        int c;
        ref char s = ref MemoryMarshal.GetReference(span);
        while ((c = s) != 0)
        {
            hash1 = ((hash1 << 5) + hash1) ^ c;
            c = Unsafe.Add(ref s, 1);
            if (c == 0)
                break;
            hash2 = ((hash2 << 5) + hash2) ^ c;
            s = ref Unsafe.Add(ref s, 2);
        }

        return hash1 + hash2 * 1566083941;
    }

    /// <summary>
    /// Gets assembly qualified name for cross-assembly references.
    /// </summary>
    private static string GetAssemblyQualifiedName(ITypeSymbol typeSymbol)
    {
        var displayName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var assemblyName = typeSymbol.ContainingAssembly?.Name ?? string.Empty;

        if (string.IsNullOrEmpty(assemblyName))
            return displayName;

        return $"{displayName}, {assemblyName}";
    }

    /// <summary>
    /// Gets display string for code generation (handles multi-dimensional arrays, removes nullable annotations).
    /// Ported from NinoTypeHelper.GetDisplayString().
    /// </summary>
    private static string GetDisplayString(ITypeSymbol typeSymbol)
    {
        var pureType = typeSymbol.GetPureType().GetNormalizedTypeSymbol();
        var ret = pureType.ToDisplayString();

        // Sanitize multi-dimensional array syntax: [*,*] -> [,], [*,*,*] -> [,,], etc.
        if (ret.Contains("[*"))
        {
            var sb = new System.Text.StringBuilder(ret.Length);
            for (int i = 0; i < ret.Length; i++)
            {
                if (ret[i] == '[' && i + 1 < ret.Length && ret[i + 1] == '*')
                {
                    // Found start of multi-dimensional array syntax
                    sb.Append('[');
                    i++; // Skip the '*'

                    // Skip asterisks and commas, but preserve commas
                    while (i < ret.Length && ret[i] != ']')
                    {
                        if (ret[i] == ',')
                        {
                            sb.Append(',');
                        }

                        i++;
                    }

                    if (i < ret.Length)
                    {
                        sb.Append(']');
                    }
                }
                else
                {
                    sb.Append(ret[i]);
                }
            }

            ret = sb.ToString();
        }

        return ret;
    }

    /// <summary>
    /// Gets suggested instance variable name for a type.
    /// Ported from NinoTypeHelper.GetTypeInstanceName().
    /// </summary>
    private static string GetTypeInstanceName(ITypeSymbol typeSymbol)
    {
        var ret = typeSymbol.GetDisplayString()
            .Replace("global::", "")
            .ToLower()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .Aggregate("", (current, c) => current + c);

        return $"@{ret}";
    }

    /// <summary>
    /// Gets the NinoTypeAttribute directly declared on the type itself.
    /// </summary>
    private static AttributeData? GetDirectNinoTypeAttribute(ITypeSymbol typeSymbol)
    {
        foreach (var attribute in typeSymbol.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.Name, "NinoTypeAttribute", StringComparison.Ordinal))
            {
                return attribute;
            }
        }
        return null;
    }

    /// <summary>
    /// Parses NinoTypeAttribute data from AttributeData.
    /// </summary>
    private static NinoTypeAttributeData ParseNinoTypeAttribute(AttributeData attributeData)
    {
        return new NinoTypeAttributeData
        {
            AutoCollect = GetConstructorArgumentByName(attributeData, "autoCollect", true),
            ContainNonPublicMembers = GetConstructorArgumentByName(attributeData, "containNonPublicMembers", false),
            AllowInheritance = GetConstructorArgumentByName(attributeData, "allowInheritance", true)
        };
    }

    /// <summary>
    /// Gets constructor argument value by parameter name from AttributeData.
    /// </summary>
    private static T GetConstructorArgumentByName<T>(AttributeData? attributeData, string parameterName, T defaultValue = default!)
    {
        if (attributeData == null || attributeData.AttributeConstructor == null)
        {
            return defaultValue;
        }

        var parameters = attributeData.AttributeConstructor.Parameters;
        var arguments = attributeData.ConstructorArguments;

        // Find the index corresponding to the parameter name
        for (int i = 0; i < parameters.Length && i < arguments.Length; i++)
        {
            if (parameters[i].Name == parameterName)
            {
                var value = arguments[i].Value;
                if (value == null)
                {
                    return defaultValue;
                }

                // Type checking and conversion
                if (value is T typedValue)
                {
                    return typedValue;
                }

                return defaultValue;
            }
        }

        return defaultValue;
    }

    // ===== Enum Mapping Methods =====

    private static TypeKindDto MapTypeKind(TypeKind kind)
    {
        return kind switch
        {
            TypeKind.Unknown => TypeKindDto.Unknown,
            TypeKind.Array => TypeKindDto.Array,
            TypeKind.Class => TypeKindDto.Class,
            TypeKind.Delegate => TypeKindDto.Delegate,
            TypeKind.Dynamic => TypeKindDto.Dynamic,
            TypeKind.Enum => TypeKindDto.Enum,
            TypeKind.Error => TypeKindDto.Error,
            TypeKind.Interface => TypeKindDto.Interface,
            TypeKind.Module => TypeKindDto.Module,
            TypeKind.Pointer => TypeKindDto.Pointer,
            TypeKind.Struct => TypeKindDto.Struct,
            TypeKind.TypeParameter => TypeKindDto.TypeParameter,
            TypeKind.Submission => TypeKindDto.Submission,
            TypeKind.FunctionPointer => TypeKindDto.FunctionPointer,
            _ => TypeKindDto.Unknown
        };
    }

    private static SpecialTypeDto MapSpecialType(SpecialType specialType)
    {
        return specialType switch
        {
            SpecialType.None => SpecialTypeDto.None,
            SpecialType.System_Object => SpecialTypeDto.System_Object,
            SpecialType.System_Enum => SpecialTypeDto.System_Enum,
            SpecialType.System_MulticastDelegate => SpecialTypeDto.System_MulticastDelegate,
            SpecialType.System_Delegate => SpecialTypeDto.System_Delegate,
            SpecialType.System_ValueType => SpecialTypeDto.System_ValueType,
            SpecialType.System_Void => SpecialTypeDto.System_Void,
            SpecialType.System_Boolean => SpecialTypeDto.System_Boolean,
            SpecialType.System_Char => SpecialTypeDto.System_Char,
            SpecialType.System_SByte => SpecialTypeDto.System_SByte,
            SpecialType.System_Byte => SpecialTypeDto.System_Byte,
            SpecialType.System_Int16 => SpecialTypeDto.System_Int16,
            SpecialType.System_UInt16 => SpecialTypeDto.System_UInt16,
            SpecialType.System_Int32 => SpecialTypeDto.System_Int32,
            SpecialType.System_UInt32 => SpecialTypeDto.System_UInt32,
            SpecialType.System_Int64 => SpecialTypeDto.System_Int64,
            SpecialType.System_UInt64 => SpecialTypeDto.System_UInt64,
            SpecialType.System_Decimal => SpecialTypeDto.System_Decimal,
            SpecialType.System_Single => SpecialTypeDto.System_Single,
            SpecialType.System_Double => SpecialTypeDto.System_Double,
            SpecialType.System_String => SpecialTypeDto.System_String,
            SpecialType.System_IntPtr => SpecialTypeDto.System_IntPtr,
            SpecialType.System_UIntPtr => SpecialTypeDto.System_UIntPtr,
            SpecialType.System_Array => SpecialTypeDto.System_Array,
            SpecialType.System_Collections_IEnumerable => SpecialTypeDto.System_Collections_IEnumerable,
            SpecialType.System_Collections_Generic_IEnumerable_T => SpecialTypeDto.System_Collections_Generic_IEnumerable_T,
            SpecialType.System_Collections_Generic_IList_T => SpecialTypeDto.System_Collections_Generic_IList_T,
            SpecialType.System_Collections_Generic_ICollection_T => SpecialTypeDto.System_Collections_Generic_ICollection_T,
            SpecialType.System_Collections_IEnumerator => SpecialTypeDto.System_Collections_IEnumerator,
            SpecialType.System_Collections_Generic_IEnumerator_T => SpecialTypeDto.System_Collections_Generic_IEnumerator_T,
            SpecialType.System_Collections_Generic_IReadOnlyList_T => SpecialTypeDto.System_Collections_Generic_IReadOnlyList_T,
            SpecialType.System_Collections_Generic_IReadOnlyCollection_T => SpecialTypeDto.System_Collections_Generic_IReadOnlyCollection_T,
            SpecialType.System_Nullable_T => SpecialTypeDto.System_Nullable_T,
            SpecialType.System_DateTime => SpecialTypeDto.System_DateTime,
            SpecialType.System_Runtime_CompilerServices_IsVolatile => SpecialTypeDto.System_Runtime_CompilerServices_IsVolatile,
            SpecialType.System_IDisposable => SpecialTypeDto.System_IDisposable,
            SpecialType.System_TypedReference => SpecialTypeDto.System_TypedReference,
            SpecialType.System_ArgIterator => SpecialTypeDto.System_ArgIterator,
            SpecialType.System_RuntimeArgumentHandle => SpecialTypeDto.System_RuntimeArgumentHandle,
            SpecialType.System_RuntimeFieldHandle => SpecialTypeDto.System_RuntimeFieldHandle,
            SpecialType.System_RuntimeMethodHandle => SpecialTypeDto.System_RuntimeMethodHandle,
            SpecialType.System_RuntimeTypeHandle => SpecialTypeDto.System_RuntimeTypeHandle,
            SpecialType.System_IAsyncResult => SpecialTypeDto.System_IAsyncResult,
            SpecialType.System_AsyncCallback => SpecialTypeDto.System_AsyncCallback,
            _ => SpecialTypeDto.None
        };
    }

    private static AccessibilityDto MapAccessibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.NotApplicable => AccessibilityDto.NotApplicable,
            Accessibility.Private => AccessibilityDto.Private,
            Accessibility.ProtectedAndInternal => AccessibilityDto.ProtectedAndInternal,
            Accessibility.Protected => AccessibilityDto.Protected,
            Accessibility.Internal => AccessibilityDto.Internal,
            Accessibility.ProtectedOrInternal => AccessibilityDto.ProtectedOrInternal,
            Accessibility.Public => AccessibilityDto.Public,
            _ => AccessibilityDto.NotApplicable
        };
    }
}

/// <summary>
/// DTO for NinoTypeAttribute data.
/// </summary>
public readonly record struct NinoTypeAttributeData
{
    public bool AutoCollect { get; init; }
    public bool ContainNonPublicMembers { get; init; }
    public bool AllowInheritance { get; init; }
}

/// <summary>
/// Extension methods needed by SymbolDataExtractor and other parts of the pipeline.
/// These are minimal versions ported from NinoTypeHelper for use during symbol extraction phase.
/// </summary>
public static class SymbolExtensions
{
    public static ITypeSymbol GetPureType(this ITypeSymbol typeSymbol)
    {
        return typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
    }

    public static ITypeSymbol GetNormalizedTypeSymbol(this ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType)
        {
            // For tuple types, use the underlying type which ignores field names
            if (typeSymbol.IsTupleType)
            {
                return namedType.TupleUnderlyingType ?? typeSymbol;
            }

            // For generic types, recursively normalize type arguments
            if (namedType.TypeArguments.Length > 0)
            {
                var normalizedArgs = namedType.TypeArguments.Select(GetNormalizedTypeSymbol).ToArray();

                // Check if any type arguments were actually normalized
                bool hasChanges = false;
                for (int i = 0; i < namedType.TypeArguments.Length; i++)
                {
                    if (!SymbolEqualityComparer.Default.Equals(namedType.TypeArguments[i], normalizedArgs[i]))
                    {
                        hasChanges = true;
                        break;
                    }
                }

                // If type arguments were normalized, construct a new generic type
                if (hasChanges)
                {
                    return namedType.ConstructedFrom.Construct(normalizedArgs);
                }
            }
        }

        return typeSymbol;
    }

    public static string GetNamespace(this string assemblyName)
    {
        var curNamespace = assemblyName;
        if (!string.IsNullOrEmpty(curNamespace))
            curNamespace = $"{curNamespace}.";

        var sb = new System.Text.StringBuilder();
        foreach (var c in curNamespace.Split('.'))
        {
            if (string.IsNullOrEmpty(c)) continue;
            var part = c;
            if (!char.IsLetter(part[0]) && part[0] != '_')
                sb.Append('_');

            for (int i = 0; i < part.Length; i++)
            {
                var ch = part[i];
                if (char.IsLetterOrDigit(ch) || ch == '_')
                {
                    sb.Append(ch);
                }
                else
                {
                    sb.Append('_');
                }
            }

            sb.Append('.');
        }

        sb.Append("NinoGen");
        return sb.ToString();
    }

    public static bool IsGenericTypeDefinition(this INamedTypeSymbol namedType)
    {
        return namedType.IsGenericType && SymbolEqualityComparer.Default.Equals(namedType, namedType.ConstructedFrom);
    }

    /// <summary>
    /// Extracts a constructor argument value from attribute data by parameter name.
    /// Used for extracting NinoTypeAttribute constructor parameters.
    /// </summary>
    public static T GetConstructorArgumentByName<T>(AttributeData? attributeData, string parameterName, T defaultValue = default!)
    {
        if (attributeData == null || attributeData.AttributeConstructor == null)
        {
            return defaultValue;
        }

        var parameters = attributeData.AttributeConstructor.Parameters;
        var arguments = attributeData.ConstructorArguments;

        // Find the index corresponding to the parameter name
        for (int i = 0; i < parameters.Length && i < arguments.Length; i++)
        {
            if (parameters[i].Name == parameterName)
            {
                var value = arguments[i].Value;
                if (value == null)
                {
                    return defaultValue;
                }

                // Type checking and conversion
                if (value is T typedValue)
                {
                    return typedValue;
                }

                return defaultValue;
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// Cached wrapper for GetAttributes() to avoid repeated calls.
    /// Used throughout the symbol extraction pipeline.
    /// </summary>
    public static System.Collections.Immutable.ImmutableArray<AttributeData> GetAttributesCache<T>(this T typeSymbol) where T : ISymbol
    {
        return typeSymbol.GetAttributes();
    }

    /// <summary>
    /// Gets the NinoConstructorAttribute from a method symbol (used for custom constructor detection).
    /// </summary>
    public static AttributeData? GetNinoConstructorAttribute(this IMethodSymbol? methodSymbol)
    {
        if (methodSymbol == null)
        {
            return null;
        }

        return methodSymbol.GetAttributesCache()
            .FirstOrDefault(static a => a.AttributeClass?.Name == "NinoConstructorAttribute");
    }

    /// <summary>
    /// Checks if a type is valid for Nino serialization by validating generic type arguments.
    /// Rejects type parameters, unbound generics, and invalid type argument counts.
    /// </summary>
    public static bool CheckGenericValidity(this ITypeSymbol containingType)
    {
        var toValidate = new Stack<ITypeSymbol>();
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        var current = containingType;
        while (current != null)
        {
            toValidate.Push(current);
            current = current.ContainingType;
        }

        // Validate all types
        while (toValidate.Count > 0)
        {
            var type = toValidate.Pop();

            // Skip if already visited to prevent infinite loops
            if (!visited.Add(type))
                continue;

            switch (type)
            {
                case ITypeParameterSymbol:
                    return false;

                case IArrayTypeSymbol arrayTypeSymbol:
                    toValidate.Push(arrayTypeSymbol.ElementType);
                    break;

                case INamedTypeSymbol { IsUnboundGenericType: true }:
                    return false;

                case INamedTypeSymbol { IsGenericType: true } namedTypeSymbol:
                    // Validate generic type
                    if (namedTypeSymbol.TypeArguments.Length != namedTypeSymbol.TypeParameters.Length)
                        return false;

                    foreach (var typeArg in namedTypeSymbol.TypeArguments)
                    {
                        if (typeArg.TypeKind == TypeKind.TypeParameter)
                            return false;
                    }

                    // Push type arguments to stack for validation
                    for (int i = namedTypeSymbol.TypeArguments.Length - 1; i >= 0; i--)
                    {
                        toValidate.Push(namedTypeSymbol.TypeArguments[i]);
                    }

                    break;

                case INamedTypeSymbol:
                    break;

                default:
                    if (type.IsUnmanagedType)
                        break;

                    return false;
            }
        }

        return true;
    }
}
