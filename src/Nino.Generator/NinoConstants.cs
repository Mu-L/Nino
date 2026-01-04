namespace Nino.Generator;

/// <summary>
/// Constants used throughout Nino.Generator
/// </summary>
public static class NinoConstants
{
    /// <summary>
    /// Conditional compilation symbol for weak version tolerance.
    /// When defined, allows deserialization even when field counts don't match.
    /// </summary>
    public const string WeakVersionToleranceSymbol = "WEAK_VERSION_TOLERANCE";
}
