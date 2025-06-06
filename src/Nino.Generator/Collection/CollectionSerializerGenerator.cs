using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Nino.Generator.Filter;
using Nino.Generator.Filter.Operation;
using Nino.Generator.Template;
using Array = Nino.Generator.Filter.Array;
using Nullable = Nino.Generator.Filter.Nullable;
using String = Nino.Generator.Filter.String;

namespace Nino.Generator.Collection;

public class CollectionSerializerGenerator : NinoCollectionGenerator
{
    private readonly IFilter _unmanagedFilter;

    public CollectionSerializerGenerator(
        Compilation compilation,
        List<ITypeSymbol> potentialCollectionSymbols) : base(compilation, potentialCollectionSymbols)
    {
        _unmanagedFilter = new Union().With
        (
            new Joint().With
            (
                new Not(new String()),
                new Unmanaged()
            ),
            new Interface("IEnumerable<T>", interfaceSymbol =>
            {
                var elementType = interfaceSymbol.TypeArguments[0];
                return elementType.IsUnmanagedType;
            })
        );
    }

    protected override IFilter Selector => new Joint().With
    (
        // We want to ensure the type we are using is accessible (i.e. not private)
        new Accessible(),
        // We want to ensure all generics are fully-typed
        new Not(new RawGeneric()),
        // We now collect things we want
        new Union().With
        (
            // We accept unmanaged
            new Unmanaged(),
            // We accept NinoTyped
            new NinoTyped(),
            // We accept strings
            new String(),
            // We want key-value pairs for dictionaries
            new Joint().With
            (
                new Trivial("KeyValuePair"),
                new Not(new AnyTypeArgument(symbol => !Selector.Filter(symbol)))
            ),
            // We want tuples
            new Joint().With
            (
                new Trivial("ValueTuple", "Tuple"),
                new Not(new AnyTypeArgument(symbol => !Selector.Filter(symbol)))
            ),
            // We want nullables
            new Nullable(),
            // We want enumerable (which contains array, icollection, ilist, idictionary, etc)
            new Interface("IEnumerable<T>", interfaceSymbol =>
            {
                var elementType = interfaceSymbol.TypeArguments[0];
                return Selector.Filter(elementType);
            }),
            // We want span
            new Span()
        )
    );

    protected override string ClassName => "Serializer";
    protected override string OutputFileName => "NinoSerializer.Collection.g.cs";

    protected override Action<StringBuilder, string> PublicMethod => (sb, typeFullName) =>
    {
        sb.GenerateClassSerializeMethods(typeFullName);
    };

    private string GetSerializeString(ITypeSymbol type, string value)
    {
        return _unmanagedFilter.Filter(type) || type.SpecialType == SpecialType.System_String
            ? $"writer.Write({value});"
            : $"Serialize({value}, ref writer);";
    }

    protected override List<Transformer> Transformers => new List<Transformer>
    {
        // Nullable Ninotypes
        new
        (
            "Nullable",
            // We want nullable for non-unmanaged ninotypes
            new Joint().With
            (
                new Nullable(),
                new TypeArgument(0, symbol => !symbol.IsUnmanagedType && Selector.Filter(symbol))
            )
            , symbol =>
            {
                ITypeSymbol elementType = ((INamedTypeSymbol)symbol).TypeArguments[0];
                return $$"""
                         [MethodImpl(MethodImplOptions.AggressiveInlining)]
                         public static void Serialize(this {{elementType.ToDisplayString()}}? value, ref Writer writer)
                         {
                             if (!value.HasValue)
                             {
                                 writer.Write(false);
                                 return;
                             }
                             
                             writer.Write(true);
                             {{GetSerializeString(elementType, "value.Value")}}
                         }
                         """;
            }
        ),
        // KeyValuePair Ninotypes
        new
        (
            "KeyValuePair",
            // We only want KeyValuePair for non-unmanaged ninotypes
            new Joint().With(
                new Trivial("KeyValuePair"),
                new AnyTypeArgument(symbol => !symbol.IsUnmanagedType)
            ),
            symbol =>
                GenericTupleLikeMethods(symbol,
                    ((INamedTypeSymbol)symbol).TypeArguments.ToArray(),
                    "Key", "Value")
        ),
        // Tuple Ninotypes
        new
        (
            "Tuple",
            // We only want Tuple for non-unmanaged ninotypes
            new Trivial("ValueTuple", "Tuple"),
            symbol => symbol.IsUnmanagedType
                ? ""
                : GenericTupleLikeMethods(symbol, ((INamedTypeSymbol)symbol)
                    .TypeArguments.ToArray(),
                    ((INamedTypeSymbol)symbol)
                    .TypeArguments.Select((_, i) => $"Item{i + 1}").ToArray())
        ),
        // Array Ninotypes
        new
        (
            "Array",
            new Array(arraySymbol =>
            {
                var elementType = arraySymbol.ElementType;
                return !elementType.IsUnmanagedType;
            }),
            symbol => $$"""
                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        public static void Serialize(this {{symbol.ToDisplayString()}} value, ref Writer writer)
                        {
                            if (value == null)
                            {
                                writer.Write(TypeCollector.NullCollection);
                                return;
                            }
                        
                            var span = value.AsSpan();
                            int cnt = span.Length;
                            writer.Write(TypeCollector.GetCollectionHeader(cnt));
                            
                            for (int i = 0; i < cnt; i++)
                            {
                            #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                var pos = writer.Advance(4);
                            #endif
                                {{GetSerializeString(((IArrayTypeSymbol)symbol).ElementType, "span[i]")}}
                            #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                writer.PutLength(pos);
                            #endif
                            }
                        }
                        """
        ),
        // Span Ninotypes
        new
        (
            "Span",
            new Joint().With
            (
                new Span(),
                new AnyTypeArgument(symbol => !symbol.IsUnmanagedType)
            ),
            symbol => $$"""
                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        public static void Serialize(this {{symbol.ToDisplayString()}} value, ref Writer writer)
                        {
                            if (value.IsEmpty)
                            {
                                writer.Write(TypeCollector.NullCollection);
                                return;
                            }
                        
                            int cnt = value.Length;
                            writer.Write(TypeCollector.GetCollectionHeader(cnt));
                            
                            for (int i = 0; i < cnt; i++)
                            {
                            #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                var pos = writer.Advance(4);
                            #endif
                                {{GetSerializeString(((INamedTypeSymbol)symbol).TypeArguments[0], "value[i]")}}
                            #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                writer.PutLength(pos);
                            #endif
                            }
                        }
                        """
        ),
        // non trivial IDictionary Ninotypes
        new
        (
            "NonTrivialDictionary",
            // Note that we accept non-trivial IDictionary types with unmanaged key and value types
            new Joint().With
            (
                new Interface("IDictionary<TKey, TValue>"),
                new NonTrivial("IDictionary", "IDictionary", "Dictionary")
            ),
            symbol =>
            {
                INamedTypeSymbol dictSymbol = (INamedTypeSymbol)symbol;
                var idictSymbol = dictSymbol.AllInterfaces.FirstOrDefault(i => i.Name == "IDictionary")
                                  ?? dictSymbol;
                var keyType = idictSymbol.TypeArguments[0];
                var valType = idictSymbol.TypeArguments[1];
                if (!Selector.Filter(keyType) || !Selector.Filter(valType)) return "";

                bool isUnmanaged = keyType.IsUnmanagedType && valType.IsUnmanagedType;

                string nonTrivialUnmanagedCase = """
                                                     writer.Write(item);
                                                 """;

                string fallbackCase = $$"""
                                        #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                            var pos = writer.Advance(4);
                                        #endif
                                            {{GetSerializeString(keyType, "item.Key")}}
                                            {{GetSerializeString(valType, "item.Value")}}
                                        #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                            writer.PutLength(pos);
                                        #endif
                                        """;

                IFilter equalityMethod = new ValidMethod((_, method) =>
                    method.MethodKind is MethodKind.BuiltinOperator or MethodKind.UserDefinedOperator
                    && method is { Name: "op_Equality", Parameters.Length: 2 }
                    && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type,
                        dictSymbol)
                    && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type,
                        dictSymbol));

                bool shouldHaveIfDefaultCheck = !symbol.IsValueType || equalityMethod.Filter(dictSymbol);

                string defaultCheck = $$"""
                                           if (value == {{(symbol.IsValueType ? "default" : "null")}})
                                           {
                                               writer.Write(TypeCollector.NullCollection);
                                               return;
                                           }
                                        """;

                return $$"""
                         [MethodImpl(MethodImplOptions.AggressiveInlining)]
                         public static void Serialize(this {{symbol.ToDisplayString()}} value, ref Writer writer)
                         {{{(shouldHaveIfDefaultCheck ? defaultCheck : "")}}
                         
                             int cnt = value.Count;
                             writer.Write(TypeCollector.GetCollectionHeader(cnt));
                         
                             foreach (var item in value)
                             {
                             {{(isUnmanaged ? nonTrivialUnmanagedCase : fallbackCase)}}
                             }
                         }
                         """;
            }
        ),
        // trivial IDictionary Ninotypes
        new
        (
            "TrivialDictionary",
            new Joint().With
            (
                new Interface("IDictionary<TKey, TValue>"),
                new Not(new NonTrivial("IDictionary", "IDictionary", "Dictionary")),
                new AnyTypeArgument(symbol => !symbol.IsUnmanagedType)
            ),
            symbol => $$"""
                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        public static void Serialize(this {{symbol.ToDisplayString()}} value, ref Writer writer)
                        {
                            if (value == {{(symbol.IsValueType ? "default" : "null")}})
                            {
                                writer.Write(TypeCollector.NullCollection);
                                return;
                            }
                        
                            int cnt = value.Count;
                            writer.Write(TypeCollector.GetCollectionHeader(cnt));
                        
                            foreach (var item in value)
                            {
                            #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                var pos = writer.Advance(4);
                            #endif
                                {{GetSerializeString(((INamedTypeSymbol)symbol).TypeArguments[0], "item.Key")}}
                                {{GetSerializeString(((INamedTypeSymbol)symbol).TypeArguments[1], "item.Value")}}
                            #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                writer.PutLength(pos);
                            #endif
                            }
                        }
                        """),
        // non trivial IEnumerable Ninotypes
        new
        (
            "NonTrivialEnumerable",
            // Note that we accept non-trivial IEnumerable types with unmanaged element types
            new Joint().With
            (
                // Note that array is an IEnumerable, but we don't want to generate code for it
                new Interface("IEnumerable<T>"),
                new Not(new Array()),
                // We want to exclude the ones that already have a serializer
                new Not(new NinoTyped()),
                new Not(new String()),
                new NonTrivial("IEnumerable", "ICollection", "IList", "List"),
                // Ensure to have a property called Count
                new ValidProperty((_, property) =>
                    property.Name == "Count" && property.Type.SpecialType == SpecialType.System_Int32)
            ),
            symbol =>
            {
                INamedTypeSymbol namedTypeSymbol = (INamedTypeSymbol)symbol;
                var ienumSymbol = namedTypeSymbol.AllInterfaces.FirstOrDefault(i =>
                                      i.OriginalDefinition.ToDisplayString().EndsWith("IEnumerable<T>"))
                                  ?? namedTypeSymbol;
                var elemType = ienumSymbol.TypeArguments[0];
                if (!Selector.Filter(elemType)) return "";

                bool isUnmanaged = elemType.IsUnmanagedType;

                string nonTrivialUnmanagedCase = """
                                                     writer.Write(item);
                                                 """;

                string fallbackCase = $$"""
                                        #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                              var pos = writer.Advance(4);
                                        #endif
                                              {{GetSerializeString(elemType, "item")}}
                                        #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                              writer.PutLength(pos);
                                        #endif
                                        """;

                IFilter equalityMethod = new ValidMethod((_, method) =>
                    method.MethodKind is MethodKind.BuiltinOperator or MethodKind.UserDefinedOperator
                    && method is { Name: "op_Equality", Parameters.Length: 2 }
                    && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type,
                        namedTypeSymbol)
                    && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type,
                        namedTypeSymbol));

                bool shouldHaveIfDefaultCheck = !symbol.IsValueType || equalityMethod.Filter(namedTypeSymbol);

                string defaultCheck = $$"""
                                           if (value == {{(symbol.IsValueType ? "default" : "null")}})
                                           {
                                               writer.Write(TypeCollector.NullCollection);
                                               return;
                                           }
                                        """;


                return $$"""
                         [MethodImpl(MethodImplOptions.AggressiveInlining)]
                         public static void Serialize(this {{symbol.ToDisplayString()}} value, ref Writer writer)
                         {{{(shouldHaveIfDefaultCheck ? defaultCheck : "")}}
                         
                             int cnt = value.Count;
                             writer.Write(TypeCollector.GetCollectionHeader(cnt));
                         
                             foreach (var item in value)
                             {
                             {{(isUnmanaged ? nonTrivialUnmanagedCase : fallbackCase)}}
                             }
                         }
                         """;
            }
        ),
        // trivial IEnumerable Ninotypes
        new
        (
            "TrivialEnumerable",
            new Joint().With
            (
                // Note that array is an IEnumerable, but we don't want to generate code for it
                new Interface("IEnumerable<T>"),
                new Not(new Array()),
                new Not(new NonTrivial("IEnumerable", "IEnumerable", "ICollection", "IList", "List")),
                new AnyTypeArgument(symbol => !symbol.IsUnmanagedType)
            ),
            symbol => $$"""
                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        public static void Serialize(this {{symbol.ToDisplayString()}} value, ref Writer writer)
                        {
                            if (value == {{(symbol.IsValueType ? "default" : "null")}})
                            {
                                writer.Write(TypeCollector.NullCollection);
                                return;
                            }
                        
                            int cnt = 0;
                            int oldPos = writer.Advance(4);
                        
                            foreach (var item in value)
                            {
                                cnt++;
                            #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                var pos = writer.Advance(4);
                            #endif
                                {{GetSerializeString(((INamedTypeSymbol)symbol).TypeArguments[0], "item")}}
                            #if {{NinoTypeHelper.WeakVersionToleranceSymbol}}
                                writer.PutLength(pos);
                            #endif
                            }
                            
                            writer.PutBack(TypeCollector.GetCollectionHeader(cnt), oldPos);
                        }
                        """),
    };

    private string GenericTupleLikeMethods(ITypeSymbol type, ITypeSymbol[] types, params string[] fields)
    {
        string[] serializeValues = new string[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            serializeValues[i] = GetSerializeString(types[i], $"value.{fields[i]}");
        }

        return $$"""
                 [MethodImpl(MethodImplOptions.AggressiveInlining)]
                 public static void Serialize(this {{type.ToDisplayString()}} value, ref Writer writer)
                 {
                     {{string.Join("\n    ", serializeValues)}};
                 }
                 """;
    }
}