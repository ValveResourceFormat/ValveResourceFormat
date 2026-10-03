using System.Diagnostics;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// String token hash entry.
/// </summary>
public readonly struct GlobalSymbol : IEquatable<GlobalSymbol>
{
    /// <summary>The name the token was hashed from, empty when unset.</summary>
    public string Name => Token == 0 ? string.Empty : StringToken.GetKnownString(Token);
    /// <summary>The hashed token.</summary>
    public uint Token { get; }

    /// <summary>Hashes and records a name. An empty name is unset.</summary>
    public GlobalSymbol(string name)
    {
        // An empty name is the unset symbol rather than the hash of nothing
        Token = name.Length == 0 ? 0 : StringToken.Store(name);
    }

    /// <summary>Hashes a name without recording it, so it neither allocates nor touches the shared table. An empty name is unset.</summary>
    public static GlobalSymbol Lookup(ReadOnlySpan<char> name) => name.IsEmpty ? default : new(StringToken.Get(name), allowUnknown: true);

    /// <summary>Wraps a token, which must be known unless allowed otherwise.</summary>
    public GlobalSymbol(uint token, bool allowUnknown = false)
    {
        Debug.Assert(
            allowUnknown || StringToken.InvertedTable.ContainsKey(token),
            $"Unknown symbol {token}."
        );

        Token = token;
    }

    /// <summary>Whether this symbol holds a stored token, as opposed to being unset.</summary>
    public bool IsValid => Token != 0;

    /// <inheritdoc/>
    public override readonly string ToString() => Name;

    /// <summary>Gets the token.</summary>
    public static implicit operator uint(GlobalSymbol symbol) => symbol.Token;
    /// <summary>Wraps a token, known or not.</summary>
    public static implicit operator GlobalSymbol(uint token) => new(token, allowUnknown: true);
    /// <summary>Hashes and records a name.</summary>
    public static implicit operator GlobalSymbol(string name) => new(name);

    /// <summary>Gets the token.</summary>
    public uint ToUInt32() => Token;

    /// <summary>Wraps a token, known or not.</summary>
    public static GlobalSymbol FromUInt32(uint token) => new(token, allowUnknown: true);

    /// <summary>Hashes and records a name.</summary>
    public static GlobalSymbol FromString(string name) => new(name);

    // record struct?

    /// <inheritdoc/>
    public bool Equals(GlobalSymbol other) => Token == other.Token;
    /// <inheritdoc/>
    public override int GetHashCode() => (int)Token;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        Debug.Assert(false, $"Boxing operation on {nameof(GlobalSymbol)}. Can you avoid this?");
        return obj is GlobalSymbol other && Equals(other);
    }

    /// <summary>Whether two symbols have the same token.</summary>
    public static bool operator ==(GlobalSymbol left, GlobalSymbol right) => left.Equals(right);
    /// <summary>Whether two symbols have different tokens.</summary>
    public static bool operator !=(GlobalSymbol left, GlobalSymbol right) => !left.Equals(right);
}
