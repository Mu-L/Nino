using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Nino.Generator.Metadata;
using Nino.Generator.Template;

namespace Nino.Generator.Common;

public class TypeConstGenerator : NinoCommonGenerator
{
    public TypeConstGenerator(Compilation compilation, NinoGraph ninoGraph, List<NinoType> ninoTypes)
        : base(compilation, ninoGraph, ninoTypes)
    {
    }

    protected override void Generate(SourceProductionContext spc)
    {
        var compilation = Compilation;

        // get type full names from models (namespaces + type names)
        var serializableTypes = NinoTypes
            .Where(ninoType => ninoType.IsPolymorphic())
            .Select(type => type.TypeSymbol)
            .Where(symbol => symbol.IsInstanceType()).ToList();

        var types = new StringBuilder();
        foreach (var type in serializableTypes)
        {
            string variableName = type.GetTypeFullName().GetTypeConstName();
            types.AppendLine($"\t\t// {type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");
            types.AppendLine($"\t\tpublic const int {variableName} = {type.GetId()};");
        }

        //remove last newline
        if (types.Length > 0)
            types.Remove(types.Length - 1, 1);

        var curNamespace = compilation.AssemblyName!.GetNamespace();

        // generate code
        var code = $$"""
                     // <auto-generated/>

                     using System;

                     namespace {{curNamespace}}
                     {
                         public static class NinoTypeConst
                         {
                     {{types}}
                         }
                     }
                     """;

        spc.AddSource("NinoTypeConst.g.cs", code);
    }
}