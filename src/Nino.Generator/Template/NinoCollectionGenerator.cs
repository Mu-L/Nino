using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Nino.Generator.Filter;

namespace Nino.Generator.Template;

public abstract class NinoCollectionGenerator(
    Compilation compilation,
    List<ITypeSymbol> potentialCollectionSymbols)
    : NinoGenerator(compilation)
{
    protected class Transformer(string name, IFilter filter, Func<ITypeSymbol, string> ruleBasedGenerator)
    {
        public readonly string Name = name;
        public readonly IFilter Filter = filter;
        public readonly Func<ITypeSymbol, string> RuleBasedGenerator = ruleBasedGenerator;
    }

    protected abstract IFilter Selector { get; }
    protected abstract string ClassName { get; }
    protected abstract string OutputFileName { get; }
    protected abstract Action<StringBuilder, string>? PublicMethod { get; }
    protected abstract List<Transformer>? Transformers { get; }

    protected override void Generate(SourceProductionContext spc)
    {
        if (potentialCollectionSymbols.Count == 0) return;
        if (Transformers == null || Transformers?.Count == 0) return;

        var filteredSymbols = potentialCollectionSymbols
            .Where(Selector.Filter).ToList();

        var compilation = Compilation;
        var sb = new StringBuilder();
        sb.AppendLine(
            $"        /* Will process types: \n         " +
            $"{string.Join(", \n         ",
                filteredSymbols.Select(s => s.ToDisplayString()))}\n        */");
        sb.AppendLine("");

        HashSet<string> addedType = new HashSet<string>();
        foreach (var type in filteredSymbols)
        {
            var typeFullName = type.ToDisplayString();
            if (!addedType.Add(typeFullName)) continue;
            for (var index = 0; index < Transformers!.Count; index++)
            {
                var transformer = Transformers![index];
                if (transformer.Filter.Filter(type))
                {
                    try
                    {
                        var indent = "        ";
                        var generated = transformer.RuleBasedGenerator(type);
                        if (string.IsNullOrEmpty(generated)) continue;
                        sb.AppendLine($"#region {typeFullName} - Generated by transformer {transformer.Name}");
                        PublicMethod?.Invoke(sb, typeFullName);
                        sb.AppendLine();
                        generated = generated.Replace("\n", $"\n{indent}");
                        sb.AppendLine($"{indent}{generated}\n");
                        sb.AppendLine("#endregion");
                        sb.AppendLine();
                        break;
                    }
                    catch (Exception e)
                    {
                        throw new ApplicationException(
                            $"{OutputFileName} error: Failed to generate code for type {typeFullName} " +
                            $"using transformer[{index}] ({transformer.Name})",
                            e);
                    }
                }
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
                         public static partial class {{ClassName}}
                         {
                     {{sb}}    }
                     }
                     """;

        spc.AddSource(OutputFileName, code);
    }
}