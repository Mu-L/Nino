namespace Nino.Generator.Metadata;

/// <summary>
/// Compilation-level metadata extracted for code generation.
/// All properties are value-based for efficient incremental caching.
/// Uses record struct for consistency with other DTOs and optimal caching performance.
/// </summary>
public readonly record struct CompilationMetadata(
    string AssemblyName,
    bool IsUnityAssembly,
    bool HasNinoCoreUsage,
    string AssemblyNamespace
);