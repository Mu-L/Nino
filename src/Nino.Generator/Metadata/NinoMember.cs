using Microsoft.CodeAnalysis;

namespace Nino.Generator.Metadata;

public class NinoMember(string name, ITypeSymbol type, ISymbol memberSymbol)
{
    public string Name { get; set; } = name;
    public ITypeSymbol Type { get; set; } = type;
    public ISymbol MemberSymbol { get; set; } = memberSymbol;
    public bool IsCtorParameter { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsProperty { get; set; }
    public bool IsUtf8String { get; set; }

    public override string ToString()
    {
        return
            $"{Type.GetDisplayString()} {Name} " +
            $"[Ctor: {IsCtorParameter}, " +
            $"Private: {IsPrivate}, " +
            $"Property: {IsProperty}, " +
            $"Utf8String: {IsUtf8String}]";
    }
}