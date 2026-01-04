using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nino.Generator.Common;

/// <summary>
/// String extension methods used by code generators.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Converts a type full name to a valid constant name by replacing special characters with underscores.
    /// Example: "System.Collections.Generic.List&lt;int&gt;" becomes "System_Collections_Generic_List_int_"
    /// </summary>
    public static string GetTypeConstName(this string typeFullName)
    {
        return typeFullName.ToCharArray()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .Aggregate("", (a, b) => a + b);
    }

    /// <summary>
    /// Gets a deterministic, non-randomized hash code for a string.
    /// This hash code is consistent across app domains and processes,
    /// making it suitable for code generation scenarios where the same
    /// input must always produce the same output.
    ///
    /// Reference: https://github.com/microsoft/referencesource/blob/51cf7850defa8a17d815b4700b67116e3fa283c2/mscorlib/system/string.cs#L894C9-L949C10
    /// </summary>
    public static int GetLegacyNonRandomizedHashCode(this string str)
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
}
