namespace ValveResourceFormat.IO.Fgd;

/// <summary>
/// Kind of an entity class. It decides whether the editor draws a default box when no helper of the class is visual.
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
/// An entity class as the editor shows it: the helpers it is drawn with.
/// </summary>
public sealed class EntityClass
{
    /// <summary>Gets the name of the class.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the kind of the class.</summary>
    public EntityClassKind Kind { get; init; }

    /// <summary>Gets the box the class declares entities are drawn as, or <see langword="null"/> where it declares none.</summary>
    public (Vector3 Mins, Vector3 Maxs)? Size { get; init; }

    /// <summary>
    /// Gets the helpers the class is drawn with, in order: the ones it declares, then one for each key that is drawn.
    /// The helper of a key is given the name of the key.
    /// </summary>
    public IReadOnlyList<HelperDeclaration> Helpers { get; init; } = [];

    /// <summary>
    /// Gets the keys that name the animation the model of an entity of the class plays, the one that matters most
    /// first. The first of them an entity sets is the one played.
    /// </summary>
    public IReadOnlyList<string> PreviewAnimationKeys { get; init; } = [];

    /// <summary>
    /// Gets the names of the classes the class is based on, directly or through others.
    /// Only the classes some class of the game requires to be placed under are listed.
    /// </summary>
    public IReadOnlySet<string> BaseClasses { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the class an entity of the class has to be placed under, or <see langword="null"/> where it can be placed anywhere.
    /// </summary>
    public string? RequiredParentClass { get; init; }

    /// <summary>
    /// Gets the material the meshes of an entity of the class are drawn with instead of the materials of their faces, or
    /// <see langword="null"/> where they keep their own.
    /// </summary>
    public string? AutoApplyMaterial { get; init; }

    /// <summary>
    /// Gets whether the class has the name, or is based on a class of that name. Only the classes some class of the
    /// game requires to be placed under are known as base classes.
    /// </summary>
    /// <param name="name">Name of the class.</param>
    /// <returns>Whether it is.</returns>
    public bool DerivesFrom(string name)
        => Name.Equals(name, StringComparison.OrdinalIgnoreCase) || BaseClasses.Contains(name);
}
