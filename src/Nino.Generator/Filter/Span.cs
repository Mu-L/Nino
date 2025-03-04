using Microsoft.CodeAnalysis;

namespace Nino.Generator.Filter;

public class Span: IFilter
{
    public bool Filter(ITypeSymbol symbol)
    {
        return symbol.OriginalDefinition.ToDisplayString() == "System.Span<T>";
    }
}