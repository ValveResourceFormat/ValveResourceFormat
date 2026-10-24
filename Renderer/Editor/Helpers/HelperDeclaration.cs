using ValveKeyValue;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A helper an entity class is drawn with in the editor, as the class declares it.
/// </summary>
public sealed class HelperDeclaration
{
    /// <summary>Gets the name the helper is created by.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the arguments of the helper, in order.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets the keys of a helper declared with a block of them rather than arguments.</summary>
    public KVObject? Metadata { get; init; }
}
