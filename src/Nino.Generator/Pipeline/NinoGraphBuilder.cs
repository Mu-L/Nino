using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nino.Generator.Metadata;

namespace Nino.Generator.Pipeline;

/// <summary>
/// Builds NinoGraph from array of NinoType DTOs.
/// Replaces the old NinoGraph constructor logic with pure DTO-based graph construction.
/// </summary>
public static class NinoGraphBuilder
{
    /// <summary>
    /// Builds a complete type hierarchy graph from NinoType DTOs.
    /// Ported from old NinoGraph constructor (lines 16-137).
    /// </summary>
    public static NinoGraph Build(
        EquatableArray<NinoType> ninoTypes,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var baseTypes = new Dictionary<NinoType, List<NinoType>>();
        var subTypes = new Dictionary<NinoType, List<NinoType>>();
        var topTypes = new HashSet<NinoType>();
        var typeMap = new Dictionary<string, NinoType>();

        // Step 1: Build TypeMap and BaseTypes
        foreach (var ninoType in ninoTypes)
        {
            var name = ninoType.TypeInfo.DisplayName;
            if (typeMap.ContainsKey(name))
                continue;

            typeMap[name] = ninoType;

            var baseTypesList = new List<NinoType>();
            baseTypes[ninoType] = baseTypesList;

            // Recursively find all base types using ParentTypeIds
            TraverseBaseTypes(ninoType, baseTypesList, ninoTypes);

            // If no base types, it's a top type
            if (baseTypesList.Count == 0)
            {
                topTypes.Add(ninoType);
            }
        }

        // Step 2: Build SubTypes (inverse of BaseTypes)
        foreach (var kvp in baseTypes)
        {
            var derivedType = kvp.Key;
            var bases = kvp.Value;

            foreach (var baseType in bases)
            {
                if (!subTypes.ContainsKey(baseType))
                {
                    subTypes[baseType] = new List<NinoType>();
                }

                if (!subTypes[baseType].Contains(derivedType))
                {
                    subTypes[baseType].Add(derivedType);
                }
            }
        }

        // Step 3: Detect circular types
        var circularTypes = CircularTypeDetector.Detect(ninoTypes, baseTypes);

        return new NinoGraph
        {
            BaseTypes = baseTypes,
            SubTypes = subTypes,
            TopTypes = topTypes,
            CircularTypes = circularTypes,
            TypeMap = typeMap
        };
    }

    /// <summary>
    /// Recursively traverses parent relationships to build the complete base type list.
    /// Converts ParentTypeIds to actual NinoType references by looking them up in allTypes.
    /// </summary>
    private static void TraverseBaseTypes(
        NinoType type,
        List<NinoType> accumulator,
        EquatableArray<NinoType> allTypes)
    {
        if (type.ParentTypeIds.Length == 0)
        {
            return;
        }

        foreach (var parentId in type.ParentTypeIds)
        {
            // Find parent NinoType by TypeId
            var parent = FindTypeById(allTypes, parentId);

            // If parent not found (external type, not a NinoType), skip
            if (parent.Equals(default(NinoType)))
                continue;

            // Avoid duplicates
            if (accumulator.Contains(parent))
                continue;

            accumulator.Add(parent);

            // Recursively traverse parent's parents
            TraverseBaseTypes(parent, accumulator, allTypes);
        }
    }

    /// <summary>
    /// Finds a NinoType by TypeId in the collection.
    /// Returns default(NinoType) if not found.
    /// </summary>
    private static NinoType FindTypeById(EquatableArray<NinoType> types, int typeId)
    {
        foreach (var type in types)
        {
            if (type.TypeInfo.TypeId == typeId)
            {
                return type;
            }
        }

        return default;
    }
}
