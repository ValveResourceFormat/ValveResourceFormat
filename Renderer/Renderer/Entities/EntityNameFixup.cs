using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using Entity = ValveResourceFormat.ResourceTypes.EntityLump.Entity;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// What a spawn group puts in place of the markers its entities' names were compiled with: <c>[PR#]</c>
/// becomes <see cref="Parent"/> and <c>&amp;0000</c> becomes <see cref="Local"/>. A map and anything it
/// loads by plain reference leave both empty, which simply drops the markers.
/// </summary>
/// <param name="Parent">Replaces <c>[PR#]</c>.</param>
/// <param name="Local">Replaces <c>&amp;0000</c>.</param>
public readonly record struct EntityNameFixup(string Parent, string Local)
{
    /// <summary>Gets the fixup that only drops the markers.</summary>
    public static EntityNameFixup None { get; } = new(string.Empty, string.Empty);

    /// <summary>Replaces the first of each marker in a name.</summary>
    public string Apply(string name) => EntityLump.ApplyNameFixup(name, Parent, Local);

    /// <summary>
    /// Gets the keyvalues with every top level string fixed up, or <paramref name="data"/> itself when none
    /// holds a marker.
    /// </summary>
    /// <remarks>
    /// Nested values are left alone: the engine only fixes the strings inside them that are typed as entity
    /// names, a type the parsed keyvalues no longer carry.
    /// </remarks>
    public Entity Apply(Entity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Entity? fixedUp = null;

        foreach (var (key, value) in data.Children)
        {
            // The same instance comes back when there is nothing to replace
            if (value.ValueType == KVValueType.String && (string)value is var authored && Apply(authored) is var name && !ReferenceEquals(name, authored))
            {
                fixedUp ??= Copy(data);
                fixedUp[key] = name;
            }
        }

        return fixedUp ?? data;
    }

    private static Entity Copy(Entity data)
    {
        var copy = new Entity { ParentLump = data.ParentLump };

        foreach (var (key, value) in data.Children)
        {
            copy.Add(key, value);
        }

        return copy;
    }
}
