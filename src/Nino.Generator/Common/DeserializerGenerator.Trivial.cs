using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;

namespace Nino.Generator.Common;

public partial class DeserializerGenerator
{
    private void GenerateTrivialCode(SourceProductionContext spc, HashSet<ITypeSymbol> validTypes,
        HashSet<ITypeSymbol> generatedTypes)
    {
        var compilation = Compilation;
        var sb = new StringBuilder();
        sb.GenerateClassDeserializeMethods("string");
        HashSet<string> generatedTypeNames = new();
        HashSet<ITypeSymbol> validTypeNames = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var type in validTypes)
        {
            validTypeNames.Add(type);
        }

        foreach (var ninoType in NinoTypes)
        {
            try
            {
                if (ninoType.TypeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
                {
                    if (namedType.TypeArguments.Any(t => !ValidType(t, validTypeNames)))
                    {
                        continue;
                    }
                }

                if (!ninoType.TypeSymbol.IsUnmanagedType)
                {
                    bool hasInvalidMember = false;
                    foreach (var member in ninoType.Members)
                    {
                        if (ValidType(member.Type, validTypeNames)) continue;

                        spc.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor("NINO001", "Nino Generator",
                                "Nino cannot find suitable deserializer for member type '{0}' in type '{1}'",
                                "Nino.Generator",
                                DiagnosticSeverity.Error, true),
                            member.MemberSymbol.Locations.First(),
                            member.Type.GetDisplayString(), ninoType.TypeSymbol.GetDisplayString()));
                        hasInvalidMember = true;
                    }

                    if (hasInvalidMember) continue;
                }

                if (!generatedTypes.Add(ninoType.TypeSymbol))
                    continue;
                if (!generatedTypeNames.Add(ninoType.TypeSymbol.GetDisplayString()))
                    continue;

                if (!ninoType.TypeSymbol.IsInstanceType() ||
                    !string.IsNullOrEmpty(ninoType.CustomDeserializer))
                    continue;
                sb.AppendLine();
                sb.AppendLine($$"""
                                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                        [EditorBrowsable(EditorBrowsableState.Never)]
                                        [System.Diagnostics.DebuggerNonUserCode]
                                        [System.Runtime.CompilerServices.CompilerGenerated]
                                        public static void DeserializeImpl(out {{ninoType.TypeSymbol.GetTypeFullName()}} value, ref Reader reader)
                                        {
                                        #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                           if (reader.Eof)
                                           {
                                              value = default;
                                              return;
                                           }
                                        #endif
                                """);

                if (ninoType.IsPolymorphic())
                {
                    sb.AppendLine("            reader.Read(out int typeId);");
                    sb.AppendLine("            if(typeId == TypeCollector.Null)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                value = default;");
                    sb.AppendLine("                return;");
                    sb.AppendLine("            }");
                    sb.AppendLine(
                        $"            else if(typeId != NinoTypeConst.{ninoType.TypeSymbol.GetTypeFullName().GetTypeConstName()})");
                    sb.AppendLine("                throw new InvalidOperationException(\"Invalid type id\");");
                    sb.AppendLine();
                }


                if (ninoType.TypeSymbol.IsUnmanagedType)
                {
                    sb.AppendLine("            reader.Read(out value);");
                }
                else
                {
                    CreateInstance(spc, sb, validTypes, ninoType, "value");
                }

                sb.AppendLine("        }");
                sb.AppendLine();
            }
            catch (Exception e)
            {
                sb.AppendLine($"/* Error: {e.Message} for type {ninoType.TypeSymbol.GetTypeFullName()}");
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
                     using System.ComponentModel;
                     using System.Collections.Generic;
                     using System.Collections.Concurrent;
                     using System.Runtime.InteropServices;
                     using System.Runtime.CompilerServices;

                     namespace {{curNamespace}}
                     {
                         public static partial class Deserializer
                         {
                     {{GeneratePrivateDeserializeImplMethodBody("string", "        ")}}
                             
                     {{sb}}    }
                     }
                     """;

        spc.AddSource($"{curNamespace}.Deserializer.g.cs", code);
    }


    private void WriteMembersWithCustomConstructor(SourceProductionContext spc, HashSet<ITypeSymbol> validTypes,
        StringBuilder sb, NinoType nt,
        string valName,
        string[] constructorMember,
        IMethodSymbol constructor)
    {
        List<(string, string)> vars = new List<(string, string)>();
        List<(string, string, bool)> privateVars = new List<(string, string, bool)>();
        Dictionary<string, string> args = new Dictionary<string, string>();
        Dictionary<string, string> tupleMap = new Dictionary<string, string>();

        List<string> valNames = new();
        List<List<NinoMember>> groups = nt.GroupByPrimitivity().ToList();
        for (var index1 = 0; index1 < groups.Count; index1++)
        {
            var members = groups[index1];
            valNames.Clear();
            foreach (var member in members)
            {
                var name = member.Name;
                var declaredType = member.Type;
                var isCtorParam = member.IsCtorParameter;
                var isPrivate = member.IsPrivate;
                var isProperty = member.IsProperty;

                var t = declaredType.GetDisplayString().Select(c => char.IsLetterOrDigit(c) ? c : '_')
                    .Aggregate("", (a, b) => a + b);
                var tempName = $"{t}_temp_{name}";


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

                valNames.Add(tempName);
            }

            if (members.Count == 1)
            {
                var member = members[0];
                var declaredType = member.Type;

                var tempName = valNames[0];
                //check if the typesymbol declaredType is string
                if (declaredType.SpecialType == SpecialType.System_String)
                {
                    var isUtf8 = member.IsUtf8String;

                    var str = isUtf8
                        ? $"reader.ReadUtf8(out {tempName});"
                        : $"reader.Read(out {tempName});";

                    //weak version tolerance
                    var toleranceCode = $$$"""
                                                       {{{declaredType.GetDisplayString()}}} {{{tempName}}} = default;
                                                       #if {{{NinoTypeHelper.WeakVersionToleranceSymbol}}}
                                                       if (!reader.Eof)
                                                       {
                                                          {{{str}}}
                                                       }
                                                       #else
                                                       {{{str}}}
                                                       #endif
                                           """;

                    sb.AppendLine(toleranceCode);
                    sb.AppendLine();
                }
                else if (declaredType.IsUnmanagedType &&
                         (!NinoGraph.TypeMap.TryGetValue(declaredType.GetDisplayString(), out var ninoType) ||
                          !ninoType.IsPolymorphic()))
                {
                    var str = $"reader.Read(out {tempName});";
                    //weak version tolerance
                    var toleranceCode = $$$"""
                                                       {{{declaredType.GetDisplayString()}}} {{{tempName}}} = default;
                                                       #if {{{NinoTypeHelper.WeakVersionToleranceSymbol}}}
                                                       if (!reader.Eof)
                                                       {
                                                          {{{str}}}
                                                       }
                                                       #else
                                                       {{{str}}}
                                                       #endif
                                           """;

                    sb.AppendLine(toleranceCode);
                    sb.AppendLine();
                }
                else
                {
                    // pre-generated
                    if (validTypes.Contains(declaredType))
                    {
                        sb.AppendLine(
                            $"            Deserialize(out {declaredType.GetDisplayString()} {tempName}, ref reader);");
                    }
                    // bottom type
                    else if (NinoGraph.TypeMap.TryGetValue(declaredType.GetDisplayString(), out var memberNinoType) &&
                             !NinoGraph.SubTypes.ContainsKey(memberNinoType))
                    {
                        // cross project referenced ninotype
                        if (!string.IsNullOrEmpty(memberNinoType.CustomDeserializer))
                        {
                            // for the sake of unity asmdef, fallback to dynamic resolve
                            sb.AppendLine("#if UNITY_2020_3_OR_NEWER");
                            sb.AppendLine(
                                $"            NinoDeserializer.Deserialize(out {declaredType.GetDisplayString()} {tempName}, ref reader);");
                            // net core project
                            sb.AppendLine("#else");
                            sb.AppendLine(
                                $"            {memberNinoType.CustomDeserializer}.DeserializeImpl(out {declaredType.GetDisplayString()} {tempName}, ref reader);");
                            sb.AppendLine("#endif");
                        }
                        // the impl is implemented in the same assembly
                        else
                        {
                            sb.AppendLine(
                                $"            DeserializeImpl(out {declaredType.GetDisplayString()} {tempName}, ref reader);");
                        }
                    }
                    // dynamically resolved type
                    else
                    {
                        sb.AppendLine(
                            $"            NinoDeserializer.Deserialize(out {declaredType.GetDisplayString()} {tempName}, ref reader);");
                    }
                }
            }
            else
            {
                sb.AppendLine($"#if {NinoTypeHelper.WeakVersionToleranceSymbol}");
                for (var index = 0; index < valNames.Count; index++)
                {
                    var val = valNames[index];
                    sb.AppendLine(
                        $"            {members[index].Type.GetDisplayString()} {val} = default;");
                    sb.AppendLine(
                        $"            if (!reader.Eof) reader.Read(out {val});");
                }

                sb.AppendLine("#else");
                sb.AppendLine(
                    $"            reader.Read(out NinoTuple<{string.Join(", ",
                        Enumerable.Range(0, valNames.Count)
                            .Select(i => $"{members[i].Type.GetDisplayString()}"))}> t{index1});");

                for (int i = 0; i < members.Count; i++)
                {
                    var name = members[i].Name;
                    tupleMap[name] = $"t{index1}.Item{i + 1}";
                }

                sb.AppendLine("#endif");
            }
        }

        List<string> ctorArgs = new List<string>();

        string? missingArg = null;
        foreach (var m in constructorMember)
        {
            var k = args.Keys.FirstOrDefault(k =>
                k.ToLower().Equals(m.ToLower()));
            if (k != null)
            {
                ctorArgs.Add(k);
            }
            else
            {
                sb.AppendLine($"            // missing constructor member {m}");
                missingArg = m;
                spc.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("NINO001", "Nino Generator",
                        "Missing constructor member {0} for {1}",
                        "Nino.Generator",
                        DiagnosticSeverity.Error, true), constructor.Locations[0],
                    m, nt.TypeSymbol.GetDisplayString()));
                break;
            }
        }

        if (missingArg != null)
        {
            sb.AppendLine(
                $"            throw new InvalidOperationException(\"Missing constructor member {missingArg}\");");
            return;
        }

        var ctorStmt = constructor.MethodKind == MethodKind.Constructor
            ? $"new {nt.TypeSymbol.GetDisplayString()}"
            : $"{nt.TypeSymbol.GetDisplayString()}.{constructor.Name}";
        if (args.Keys.Any(tupleMap.ContainsKey))
        {
            sb.AppendLine($"#if {NinoTypeHelper.WeakVersionToleranceSymbol}");
            sb.AppendLine(
                $"            {valName} = {ctorStmt}({string.Join(", ", ctorArgs.Select(k => args[k]))}){(vars.Count > 0 ? "" : ";")}");
            sb.AppendLine("#else");
            sb.AppendLine(
                $"            {valName} = {ctorStmt}({string.Join(", ", ctorArgs.Select(k =>
                {
                    if (!tupleMap.TryGetValue(k, out var value))
                    {
                        return args[k];
                    }

                    return value;
                }))}){(vars.Count > 0 ? "" : ";")}");
            sb.AppendLine("#endif");
        }
        else
        {
            sb.AppendLine(
                $"            {valName} = {ctorStmt}({string.Join(", ", ctorArgs.Select(k => args[k]))}){(vars.Count > 0 ? "" : ";")}");
        }

        if (vars.Count > 0)
        {
            string padding = new string(' ', valName.Length);
            sb.AppendLine($"            {padding}   {{");
            foreach (var (memberName, varName) in vars)
            {
                if (tupleMap.TryGetValue(memberName, out var value))
                {
                    sb.AppendLine($"#if {NinoTypeHelper.WeakVersionToleranceSymbol}");
                    sb.AppendLine(
                        $"            {padding}      \t{memberName} = {varName},");
                    sb.AppendLine("#else");
                    sb.AppendLine(
                        $"            {padding}      \t{memberName} = {value},");
                    sb.AppendLine("#endif");
                }
                else
                {
                    sb.AppendLine(
                        $"            {padding}      \t{memberName} = {varName},");
                }
            }

            sb.AppendLine($"            {padding}   }};");
        }

        if (privateVars.Count > 0)
        {
            var originalValName = valName;

            if (nt.TypeSymbol.IsValueType)
            {
                valName = $"ref {valName}";
            }

            static void AppendAccess(StringBuilder sb, bool isProperty, string memberName, string valName,
                string varName)
            {
                if (isProperty)
                {
                    sb.AppendLine(
                        $"            PrivateAccessor.__set__{memberName}__({valName}, {varName});");
                }
                else
                {
                    sb.AppendLine(
                        $"            ref var __{varName} = ref PrivateAccessor.__{memberName}__({valName});");
                    sb.AppendLine($"            __{varName} = {varName};");
                }
            }

            sb.AppendLine("#if NET8_0_OR_GREATER");
            foreach (var (memberName, varName, isProperty) in privateVars)
            {
                if (tupleMap.TryGetValue(memberName, out var value))
                {
                    sb.AppendLine($"#if {NinoTypeHelper.WeakVersionToleranceSymbol}");
                    AppendAccess(sb, isProperty, memberName, valName, varName);
                    sb.AppendLine("#else");
                    AppendAccess(sb, isProperty, memberName, valName, value);
                    sb.AppendLine("#endif");
                }
                else
                {
                    AppendAccess(sb, isProperty, memberName, valName, varName);
                }
            }

            sb.AppendLine("#else");
            foreach (var (memberName, varName, _) in privateVars)
            {
                var legacyVal = $"{originalValName}.__nino__generated__{memberName}";
                if (tupleMap.TryGetValue(memberName, out var value))
                {
                    sb.AppendLine($"#if {NinoTypeHelper.WeakVersionToleranceSymbol}");
                    sb.AppendLine($"            {legacyVal} = {varName};");
                    sb.AppendLine("#else");
                    sb.AppendLine($"            {legacyVal} = {value};");
                    sb.AppendLine("#endif");
                }
                else
                {
                    sb.AppendLine($"            {legacyVal} = {varName};");
                }
            }

            sb.AppendLine("#endif");
        }
    }

    private void CreateInstance(SourceProductionContext spc, StringBuilder sb, HashSet<ITypeSymbol> validTypes,
        NinoType nt, string valName)
    {
        //if this subtype contains a custom constructor, use it
        //go through all constructors and find the one with the NinoConstructor attribute
        //get constructors of the symbol
        var constructors = (nt.TypeSymbol as INamedTypeSymbol)?.Constructors.ToList();

        // append static methods that return an instance of the type
        constructors ??= [];
        constructors.AddRange(nt.TypeSymbol.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public &&
                        m.IsStatic &&
                        SymbolEqualityComparer.Default.Equals(m.ReturnType, nt.TypeSymbol)));

        if (constructors.Count == 0)
        {
            sb.AppendLine(
                $"            // no constructor found, symbol is not a named type symbol but a {nt.TypeSymbol.GetType()}");
            sb.AppendLine(
                $"            throw new InvalidOperationException(\"No constructor found for {nt.TypeSymbol.GetDisplayString()}\");");
            return;
        }

        IMethodSymbol? constructor = null;

        // if typesymbol is a record, try get the primary constructor
        if (nt.TypeSymbol.IsRecord)
        {
            constructor = constructors.FirstOrDefault(c => c.Parameters.Length == 0 || c.Parameters.All(p =>
                nt.Members.Any(m => m.Name == p.Name)));
        }

        if (constructor == null)
            constructor = constructors.OrderBy(c => c.Parameters.Length).FirstOrDefault();

        var custom = constructors.FirstOrDefault(c => c.GetAttributesCache().Any(a =>
            a.AttributeClass != null &&
            a.AttributeClass.GetDisplayString().EndsWith("NinoConstructorAttribute")));
        if (custom != null)
        {
            constructor = custom;
        }

        if (constructor == null)
        {
            sb.AppendLine("            // no constructor found");
            sb.AppendLine(
                $"            throw new InvalidOperationException(\"No constructor found for {nt.TypeSymbol.GetDisplayString()}\");");
            return;
        }

        sb.AppendLine($"            // use {constructor.ToDisplayString()}");

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

        WriteMembersWithCustomConstructor(spc, validTypes, sb, nt, valName, args, constructor);
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