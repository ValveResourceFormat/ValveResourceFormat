using System.Diagnostics;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// String token hash entry.
/// </summary>
readonly struct GlobalSymbol : IEquatable<GlobalSymbol>
{
    public string Name => StringToken.GetKnownString(Token);
    public uint Token { get; }

    public GlobalSymbol(string name)
    {
        Token = StringToken.Store(name);
    }

    public GlobalSymbol(uint token, bool allowUnknown = false)
    {
        Debug.Assert(
            allowUnknown || StringToken.InvertedTable.ContainsKey(token),
            $"Unknown symbol {token}."
        );

        Token = token;
    }

    public override readonly string ToString() => Name;

    public static implicit operator uint(GlobalSymbol symbol) => symbol.Token;
    public static implicit operator GlobalSymbol(uint token) => new(token, allowUnknown: true);
    public static implicit operator GlobalSymbol(string name) => new(name);

    // record struct?

    public bool Equals(GlobalSymbol other) => Token == other.Token;
    public override int GetHashCode() => (int)Token;

    public override bool Equals(object? obj)
    {
        Debug.Assert(false, $"Boxing operation on {nameof(GlobalSymbol)}. Can you avoid this?");
        return obj is GlobalSymbol other && Equals(other);
    }

    public static bool operator ==(GlobalSymbol left, GlobalSymbol right) => left.Equals(right);
    public static bool operator !=(GlobalSymbol left, GlobalSymbol right) => !left.Equals(right);
}
