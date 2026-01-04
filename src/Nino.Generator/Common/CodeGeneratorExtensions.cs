using System.Runtime.CompilerServices;
using System.Text;

namespace Nino.Generator.Common;

/// <summary>
/// Extension methods for StringBuilder used in code generation.
/// Provides helpers for generating serialization/deserialization method signatures.
/// </summary>
public static class CodeGeneratorExtensions
{
    /// <summary>
    /// Generates public Serialize extension methods for a type.
    /// Creates both array-returning and INinoBufferWriter-accepting overloads.
    /// </summary>
    /// <param name="sb">StringBuilder to append to</param>
    /// <param name="typeFullName">Fully qualified type name</param>
    /// <param name="typeParam">Generic type parameters (e.g., "&lt;T&gt;")</param>
    /// <param name="genericConstraint">Generic constraints (e.g., "where T : struct")</param>
    public static void GenerateClassSerializeMethods(this StringBuilder sb, string typeFullName, string typeParam = "",
        string genericConstraint = "")
    {
        var indent = "        ";
        var ret = $$"""
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    public static byte[] Serialize{{typeParam}}(this {{typeFullName}} value) {{genericConstraint}}
                    {
                        var bufferWriter = NinoSerializer.GetBufferWriter();
                        Serialize(value, bufferWriter);
                        var ret = bufferWriter.WrittenSpan.ToArray();
                        NinoSerializer.ReturnBufferWriter(bufferWriter);
                        return ret;
                    }

                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    public static void Serialize{{typeParam}}(this {{typeFullName}} value, INinoBufferWriter bufferWriter) {{genericConstraint}}
                    {
                        Writer writer = new Writer(bufferWriter);
                        Serialize(value, ref writer);
                    }
                    """;

        ret = ret.Replace("\n", $"\n{indent}");
        sb.AppendLine();
        sb.AppendLine($"{indent}{ret}");
        sb.AppendLine();
    }

    /// <summary>
    /// Generates public Deserialize extension method for a type.
    /// </summary>
    /// <param name="sb">StringBuilder to append to</param>
    /// <param name="typeFullName">Fully qualified type name</param>
    /// <param name="typeParam">Generic type parameters (e.g., "&lt;T&gt;")</param>
    /// <param name="genericConstraint">Generic constraints (e.g., "where T : struct")</param>
    public static void GenerateClassDeserializeMethods(this StringBuilder sb, string typeFullName,
        string typeParam = "",
        string genericConstraint = "")
    {
        sb.AppendLine($$"""
                                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                public static void Deserialize{{typeParam}}(ReadOnlySpan<byte> data, out {{typeFullName}} value) {{genericConstraint}}
                                {
                                    var reader = new Reader(data);
                                    Deserialize(out value, ref reader);
                                }
                        """);
        sb.AppendLine();
    }
}
