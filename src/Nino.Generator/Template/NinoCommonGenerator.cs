using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;

namespace Nino.Generator.Template;

public abstract class NinoCommonGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    NinoGraph ninoGraph,
    EquatableArray<NinoType> ninoTypes,
    bool isUnityAssembly = false)
    : NinoGenerator(typeInfoCache, assemblyNamespace, isUnityAssembly)
{
    protected readonly NinoGraph NinoGraph = ninoGraph;
    protected readonly EquatableArray<NinoType> NinoTypes = ninoTypes;
}