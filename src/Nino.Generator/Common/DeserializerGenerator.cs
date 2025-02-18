using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Nino.Generator.Template;

namespace Nino.Generator.Common;

public class DeserializerGenerator(
    Compilation compilation,
    List<ITypeSymbol> ninoSymbols,
    Dictionary<string, List<string>> inheritanceMap,
    Dictionary<string, List<string>> subTypeMap,
    ImmutableArray<string> topNinoTypes)
    : NinoCommonGenerator(compilation, ninoSymbols, inheritanceMap, subTypeMap, topNinoTypes)
{
    protected override void Generate(SourceProductionContext spc)
    {
        var compilation = Compilation;
        var ninoSymbols = NinoSymbols;
        var inheritanceMap = InheritanceMap;
        var subTypeMap = SubTypeMap;

        var sb = new StringBuilder();

        sb.GenerateClassDeserializeMethods("T?", "<T>", "where T : unmanaged");
        sb.GenerateClassDeserializeMethods("T[]", "<T>", "where T : unmanaged");
        sb.GenerateClassDeserializeMethods("T?[]", "<T>", "where T : unmanaged");
        sb.GenerateClassDeserializeMethods("List<T>", "<T>", "where T : unmanaged");
        sb.GenerateClassDeserializeMethods("List<T?>", "<T>", "where T : unmanaged");
        sb.GenerateClassDeserializeMethods("Dictionary<TKey, TValue>", "<TKey, TValue>",
            "where TKey : unmanaged where TValue : unmanaged");
        sb.GenerateClassDeserializeMethods("string");

        foreach (var typeSymbol in ninoSymbols)
        {
            try
            {
                string typeFullName = typeSymbol.GetTypeFullName();
                GenerateDeserializeImplementation(typeSymbol, typeFullName, sb, inheritanceMap,
                    subTypeMap, ninoSymbols);
            }
            catch (Exception e)
            {
                sb.AppendLine($"/* Error: {e.Message} for type {typeSymbol.GetTypeFullName()}");
                //add stacktrace
                foreach (var line in e.StackTrace.Split('\n'))
                {
                    sb.AppendLine($" * {line}");
                }

                //end error
                sb.AppendLine(" */");
            }
        }

        var curNamespace = compilation.AssemblyName!.GetNamespace();

        // generate code
        var code = $$"""
                     // <auto-generated/>

                     using System;
                     using global::Nino.Core;
                     using System.Buffers;
                     using System.Collections.Generic;
                     using System.Collections.Concurrent;
                     using System.Runtime.InteropServices;
                     using System.Runtime.CompilerServices;

                     namespace {{curNamespace}}
                     {
                         public static partial class Deserializer
                         {
                     {{GeneratePrivateDeserializeImplMethodBody("T", "        ", "<T>", "where T : unmanaged")}}
                            
                             [MethodImpl(MethodImplOptions.AggressiveInlining)]
                             public static void Deserialize<T>(ReadOnlySpan<byte> data, out T value) where T : unmanaged
                             {
                                 value = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(data));
                             }

                     {{GeneratePrivateDeserializeImplMethodBody("T[]", "        ", "<T>", "where T : unmanaged")}}

                     {{GeneratePrivateDeserializeImplMethodBody("List<T>", "        ", "<T>", "where T : unmanaged")}}

                     {{GeneratePrivateDeserializeImplMethodBody("IList<T>", "        ", "<T>", "where T : unmanaged")}}

                     {{GeneratePrivateDeserializeImplMethodBody("ICollection<T>", "        ", "<T>", "where T : unmanaged")}}

                     {{GeneratePrivateDeserializeImplMethodBody("T?", "        ", "<T>", "where T : unmanaged")}}

                     {{GeneratePrivateDeserializeImplMethodBody("T?[]", "        ", "<T>", "where T : unmanaged")}}
                             
                     {{GeneratePrivateDeserializeImplMethodBody("List<T?>", "        ", "<T>", "where T : unmanaged")}}

                     {{GeneratePrivateDeserializeImplMethodBody("IList<T?>", "        ", "<T>", "where T : unmanaged")}}

                     {{GeneratePrivateDeserializeImplMethodBody("ICollection<T?>", "        ", "<T>", "where T : unmanaged")}}

                     {{GeneratePrivateDeserializeImplMethodBody("Dictionary<TKey, TValue>", "        ", "<TKey, TValue>", "where TKey : unmanaged where TValue : unmanaged")}}

                     {{GeneratePrivateDeserializeImplMethodBody("IDictionary<TKey, TValue>", "        ", "<TKey, TValue>", "where TKey : unmanaged where TValue : unmanaged")}}

                     {{GeneratePrivateDeserializeImplMethodBody("string", "        ")}}
                             
                     {{sb}}    }
                     }
                     """;

        spc.AddSource("NinoDeserializer.g.cs", code);
    }

    private static void GenerateDeserializeImplementation(ITypeSymbol typeSymbol, string typeFullName, StringBuilder sb,
        Dictionary<string, List<string>> inheritanceMap,
        Dictionary<string, List<string>> subTypeMap, List<ITypeSymbol> ninoSymbols)
    {
        bool isPolymorphicType = typeSymbol.IsPolymorphicType();

        // check if struct is unmanaged
        if (typeSymbol.IsUnmanagedType && !isPolymorphicType)
        {
            return;
        }

        sb.GenerateClassDeserializeMethods(typeFullName);

        sb.AppendLine($$"""
                                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                public static void Deserialize(out {{typeFullName}} value, ref Reader reader)
                                {
                                #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                     if (reader.Eof)
                                     {
                                        value = default;
                                        return;
                                     }
                                #endif
                                     
                        """);

        if (typeSymbol.IsPolymorphicType())
        {
            sb.AppendLine("            reader.Read(out int typeId);");
            sb.AppendLine();
        }

        void WriteMembersWithCustomConstructor(List<NinoTypeHelper.NinoMember> members, ITypeSymbol type,
            string typeName, string valName, string[] constructorMember)
        {
            List<(string, string)> vars = new List<(string, string)>();
            List<(string, string, bool)> privateVars = new List<(string, string, bool)>();
            Dictionary<string, string> args = new Dictionary<string, string>();
            foreach (var (name, declaredType, attrs, isCtorParam, isPrivate, isProperty) in members)
            {
                var t = declaredType.ToDisplayString().Select(c => char.IsLetterOrDigit(c) ? c : '_')
                    .Aggregate("", (a, b) => a + b);
                var tempName = $"{t}_temp_{name}";
                //check if the typesymbol declaredType is string
                if (declaredType.SpecialType == SpecialType.System_String)
                {
                    //check if this member is annotated with [NinoUtf8]
                    var isUtf8 = attrs.Any(a =>
                        a.AttributeClass?.ToDisplayString().EndsWith("NinoUtf8Attribute") == true);

                    sb.AppendLine(
                        isUtf8
                            ? $"                    reader.ReadUtf8(out {declaredType.ToDisplayString()} {tempName});"
                            : $"                    reader.Read(out {declaredType.ToDisplayString()} {tempName});");
                }
                else
                {
                    sb.AppendLine(
                        $"                    {declaredType.GetDeserializePrefix()}(out {declaredType.ToDisplayString()} {tempName}, ref reader);");
                }

                if (constructorMember.Any(c => c.ToLower().Equals(name.ToLower())))
                {
                    args.Add(name, tempName);
                }
                else
                {
                    // we dont want init-only properties from the primary constructor
                    if (!isCtorParam)
                    {
                        if (!isPrivate)
                        {
                            vars.Add((name, tempName));
                        }
                        else
                        {
                            privateVars.Add((name, tempName, isProperty));
                        }
                    }
                }
            }

            sb.AppendLine(
                $"                    {valName} = new {typeName}({string.Join(", ",
                    constructorMember.Select(m =>
                        args[args.Keys
                            .FirstOrDefault(k =>
                                k.ToLower()
                                    .Equals(m.ToLower()))]
                    ))}){(vars.Count > 0 ? "" : ";")}");
            if (vars.Count > 0)
            {
                sb.AppendLine($"                    {new string(' ', valName.Length)}   {{");
                foreach (var (memberName, varName) in vars)
                {
                    sb.AppendLine(
                        $"                 {new string(' ', valName.Length)}      \t{memberName} = {varName},");
                }

                if (privateVars.Count > 0)
                {
                    sb.AppendLine("#if !NET8_0_OR_GREATER");
                    foreach (var (memberName, varName, _) in privateVars)
                    {
                        sb.AppendLine(
                            $"                 {new string(' ', valName.Length)}      \t__nino__generated__{memberName} = {varName},");
                    }

                    sb.AppendLine("#endif");
                }

                sb.AppendLine($"                    {new string(' ', valName.Length)}   }};");
            }

            if (privateVars.Count > 0)
            {
                sb.AppendLine("#if NET8_0_OR_GREATER");
                if (type.IsValueType)
                {
                    valName = $"ref {valName}";
                }

                foreach (var (memberName, varName, isProperty) in privateVars)
                {
                    if (isProperty)
                    {
                        sb.AppendLine(
                            $"                    PrivateAccessor.__set__{memberName}__({valName}, {varName});");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"                    ref var __{varName} = ref PrivateAccessor.__{memberName}__({valName});");
                        sb.AppendLine($"                    __{varName} = {varName};");
                    }
                }

                sb.AppendLine("#endif");
            }
        }

        void CreateInstance(List<NinoTypeHelper.NinoMember> defaultMembers, ITypeSymbol symbol,
            string valName,
            string typeName)
        {
            //if this subtype contains a custom constructor, use it
            //go through all constructors and find the one with the NinoConstructor attribute
            //get constructors of the symbol
            var constructors = (symbol as INamedTypeSymbol)?.Constructors.ToList();

            if (constructors == null)
            {
                sb.AppendLine(
                    $"                    // no constructor found, symbol is not a named type symbol but a {symbol.GetType()}");
                sb.AppendLine(
                    $"                    throw new InvalidOperationException(\"No constructor found for {typeName}\");");
                return;
            }

            IMethodSymbol? constructor = null;

            // if typesymbol is a record, try get the primary constructor
            if (symbol.IsRecord)
            {
                constructor = constructors.FirstOrDefault(c => c.Parameters.Length == 0 || c.Parameters.All(p =>
                    defaultMembers.Any(m => m.Name == p.Name)));
            }

            if (constructor == null)
                constructor = constructors.OrderBy(c => c.Parameters.Length).FirstOrDefault();

            if (constructor == null)
            {
                sb.AppendLine("                    // no constructor found");
                sb.AppendLine(
                    $"                    throw new InvalidOperationException(\"No constructor found for {typeName}\");");
                return;
            }

            var custom = constructors.FirstOrDefault(c => c.GetAttributes().Any(a =>
                a.AttributeClass != null &&
                a.AttributeClass.ToDisplayString().EndsWith("NinoConstructorAttribute")));
            if (custom != null)
            {
                constructor = custom;
            }

            sb.AppendLine($"                    // use {constructor.ToDisplayString()}");

            var attr = constructor.GetNinoConstructorAttribute();
            string[] args;
            if (attr != null)
            {
                //attr is         [NinoConstructor(nameof(a), nameof(b), nameof(c), ...)]
                //we need to get a, b, c, ...
                var args0 = attr.ConstructorArguments[0].Values;
                //should be a string array
                args = args0.Select(a =>
                    a.Value as string).ToArray()!;
            }
            else
            {
                args = constructor.Parameters.Select(p => p.Name).ToArray();
            }

            WriteMembersWithCustomConstructor(defaultMembers, symbol, typeName, valName, args);
        }

        if (!subTypeMap.TryGetValue(typeFullName, out var lst))
        {
            lst = new List<string>();
        }

        //sort lst by how deep the inheritance is (i.e. how many levels of inheritance), the deepest first
        lst.Sort((a, b) =>
        {
            int aCount = inheritanceMap[a].Count;
            int bCount = inheritanceMap[b].Count;
            return bCount.CompareTo(aCount);
        });

        if (isPolymorphicType)
        {
            sb.AppendLine("            switch (typeId)");
            sb.AppendLine("            {");
            if (typeSymbol.IsReferenceType)
            {
                sb.AppendLine("""
                                              case TypeCollector.Null:
                                                  value = null;
                                                  return;
                              """);
            }
        }

        foreach (var subType in lst)
        {
            var subTypeSymbol = ninoSymbols.First(s => s.GetTypeFullName() == subType);

            if (subTypeSymbol.IsInstanceType())
            {
                string valName = subType.Replace("global::", "").Replace(".", "_").ToLower();
                sb.AppendLine(
                    $"                case NinoTypeConst.{subTypeSymbol.GetTypeFullName().GetTypeConstName()}:");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {subType} {valName};");

                if (subTypeSymbol.IsUnmanagedType)
                {
                    sb.AppendLine($"                    reader.Read(out {valName});");
                }
                else
                {
                    //get members
                    List<ITypeSymbol> subTypeParentSymbols =
                        ninoSymbols.Where(m => inheritanceMap[subType]
                            .Contains(m.GetTypeFullName())).ToList();

                    var members = subTypeSymbol.GetNinoTypeMembers(subTypeParentSymbols);
                    //get distinct members
                    members = members.Distinct().ToList();
                    CreateInstance(members, subTypeSymbol, valName, subType);
                }

                sb.AppendLine($"                    value = {valName};");
                sb.AppendLine("                    return;");
                sb.AppendLine("                }");
            }
        }

        if (typeSymbol.IsInstanceType())
        {
            if (isPolymorphicType)
            {
                sb.AppendLine($"                case NinoTypeConst.{typeSymbol.GetTypeFullName().GetTypeConstName()}:");
                sb.AppendLine("                {");
            }

            if (typeSymbol.IsUnmanagedType)
            {
                sb.AppendLine($"                    reader.Read(out value);");
            }
            else
            {
                List<ITypeSymbol> parentTypeSymbols =
                    ninoSymbols.Where(m => inheritanceMap[typeFullName]
                        .Contains(m.GetTypeFullName())).ToList();
                var defaultMembers = typeSymbol.GetNinoTypeMembers(parentTypeSymbols);
                string valName = "value";
                CreateInstance(defaultMembers, typeSymbol, valName, typeFullName);
            }

            if (isPolymorphicType)
            {
                sb.AppendLine("                    return;");
                sb.AppendLine("                }");
            }
        }

        if (isPolymorphicType)
        {
            sb.AppendLine("                default:");
            sb.AppendLine(
                "                    throw new InvalidOperationException($\"Invalid type id {typeId}\");");
            sb.AppendLine("            }");
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static string GeneratePrivateDeserializeImplMethodBody(string typeName, string indent = "",
        string typeParam = "",
        string genericConstraint = "")
    {
        var ret = $$"""
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    public static void Deserialize{{typeParam}}(out {{typeName}} value, ref Reader reader) {{genericConstraint}}
                    {
                    #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                         if (reader.Eof)
                         {
                            value = default;
                            return;
                         }
                    #endif
                        
                        reader.Read(out value);
                    }
                    """;

        // indent
        ret = ret.Replace("\n", $"\n{indent}");
        return $"{indent}{ret}";
    }
}