using ValveKeyValue;
using ValveResourceFormat.Renderer.Editor.Helpers;

namespace ValveResourceFormat.Renderer.Editor.Entities;

/// <summary>
/// What an entity of a class is in the editor, which decides what of it is drawn besides its helpers.
/// </summary>
public enum EntityClassKind
{
    /// <summary>An entity placed at a point.</summary>
    Point,

    /// <summary>An entity made of map geometry.</summary>
    Solid,

    /// <summary>A path, which is seen by its nodes.</summary>
    Path,

    /// <summary>A node of a path, which is seen by the path it is on.</summary>
    PathNode,

    /// <summary>A cable, which is seen by its nodes.</summary>
    Cable,
}

/// <summary>
/// A key an entity of a class has, with what its class declares of it.
/// </summary>
public sealed class EntityClassKey
{
    /// <summary>Gets the name of the key.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the value the key has until it is changed, or <see langword="null"/> where the class declares none.</summary>
    public string? Default { get; init; }

    /// <summary>Gets what else the class declares of the key, such as whether it names the animation shown.</summary>
    public KVObject? Metadata { get; init; }
}

/// <summary>
/// An entity class as the editor shows it: the helpers it is drawn with and the keys they read.
/// </summary>
public sealed class EntityClass
{
    /// <summary>Gets the name of the class.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what an entity of the class is.</summary>
    public EntityClassKind Kind { get; init; }

    /// <summary>Gets the color the class declares, or <see langword="null"/> where it declares none.</summary>
    public Color32? Color { get; init; }

    /// <summary>Gets the box the class declares entities are drawn as, or <see langword="null"/> where it declares none.</summary>
    public (Vector3 Mins, Vector3 Maxs)? Size { get; init; }

    /// <summary>
    /// Gets the helpers the class is drawn with, in order: the ones it declares, and then one for each of its keys
    /// that is drawn, which is given the name of the key.
    /// </summary>
    public IReadOnlyList<HelperDeclaration> Helpers { get; init; } = [];

    /// <summary>Gets the keys of the class, in the order they are declared.</summary>
    public IReadOnlyList<EntityClassKey> Keys { get; init; } = [];

    /// <summary>Gets the names of every class the class is based on, directly or through others.</summary>
    public IReadOnlySet<string> BaseClasses { get; init; } = new HashSet<string>();

    /// <summary>
    /// Gets the class an entity of the class has to be placed under, or <see langword="null"/> where it can be placed anywhere.
    /// </summary>
    public string? RequiredParentClass { get; init; }

    /// <summary>Finds a key of the class by its name, without regard to case.</summary>
    /// <param name="name">Name of the key.</param>
    /// <returns>The key, or <see langword="null"/> when the class declares nothing of it.</returns>
    public EntityClassKey? FindKey(string name)
    {
        foreach (var key in Keys)
        {
            if (key.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>Gets whether the class is the one of a name, or is based on it.</summary>
    /// <param name="name">Name of the class.</param>
    /// <returns>Whether it is.</returns>
    public bool DerivesFrom(string name)
        => Name.Equals(name, StringComparison.OrdinalIgnoreCase) || BaseClasses.Contains(name);
}
