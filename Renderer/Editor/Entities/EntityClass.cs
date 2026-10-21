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
/// An entity class as the editor shows it: the helpers it is drawn with.
/// </summary>
public sealed class EntityClass
{
    /// <summary>Gets the name of the class.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what an entity of the class is.</summary>
    public EntityClassKind Kind { get; init; }

    /// <summary>Gets the box the class declares entities are drawn as, or <see langword="null"/> where it declares none.</summary>
    public (Vector3 Mins, Vector3 Maxs)? Size { get; init; }

    /// <summary>
    /// Gets the helpers the class is drawn with, in order: the ones it declares, and then one for each of its keys
    /// that is drawn, which is given the name of the key.
    /// </summary>
    public IReadOnlyList<HelperDeclaration> Helpers { get; init; } = [];

    /// <summary>
    /// Gets the keys that name the animation the model of an entity of the class plays, the one that matters most
    /// first. The first of them an entity sets is the one played.
    /// </summary>
    public IReadOnlyList<string> PreviewAnimationKeys { get; init; } = [];

    /// <summary>
    /// Gets the names of the classes the class is based on, directly or through others, of the ones a class of its
    /// game requires to be placed under.
    /// </summary>
    public IReadOnlySet<string> BaseClasses { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the class an entity of the class has to be placed under, or <see langword="null"/> where it can be placed anywhere.
    /// </summary>
    public string? RequiredParentClass { get; init; }

    /// <summary>
    /// Gets the material the meshes of an entity of the class are drawn with in place of the ones of their faces, or
    /// <see langword="null"/> where they are drawn with their own.
    /// </summary>
    public string? AutoApplyMaterial { get; init; }

    /// <summary>
    /// Gets whether the class is the one of a name, or is based on it. Only the classes a class of its game requires
    /// to be placed under are known to be based on.
    /// </summary>
    /// <param name="name">Name of the class.</param>
    /// <returns>Whether it is.</returns>
    public bool DerivesFrom(string name)
        => Name.Equals(name, StringComparison.OrdinalIgnoreCase) || BaseClasses.Contains(name);
}
