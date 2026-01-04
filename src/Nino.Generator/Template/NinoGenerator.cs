using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;

namespace Nino.Generator.Template;

public abstract class NinoGenerator(
    Dictionary<int, TypeInfoDto> typeInfoCache,
    string assemblyNamespace,
    bool isUnityAssembly = false)
{
    protected readonly Dictionary<int, TypeInfoDto> TypeInfoCache = typeInfoCache;
    protected readonly string AssemblyNamespace = assemblyNamespace;
    protected readonly bool IsUnityAssembly = isUnityAssembly;

    protected abstract void Generate(SourceProductionContext spc);

    public void Execute(SourceProductionContext spc)
    {
        try
        {
            Generate(spc);
        }
        catch (System.Exception e)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("NINO001", "Nino Generator",
                    $"An error occurred while generating code: {e.GetType()} {e.Message}, {e.StackTrace}",
                    "Nino.Generator",
                    DiagnosticSeverity.Error, true), Location.None));
        }
    }
}