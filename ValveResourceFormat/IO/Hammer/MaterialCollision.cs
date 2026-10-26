using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.IO;

/// <summary>
/// The collision that faces with a material compile to in a map.
/// </summary>
public sealed class MaterialCollision
{
    /// <summary>Gets whether the faces have collision at all.</summary>
    public bool IsSolid { get; }

    /// <summary>Gets the collision group of the faces, or empty when the material gives none.</summary>
    public string CollisionGroup { get; }

    /// <summary>Gets the interaction layers the faces are in.</summary>
    public IReadOnlyList<string> InteractAs { get; }

    /// <summary>Gets the interaction layers the faces collide with.</summary>
    public IReadOnlyList<string> InteractWith { get; }

    /// <summary>Gets the interaction layers the faces never collide with.</summary>
    public IReadOnlyList<string> InteractExclude { get; }

    /// <summary>Gets the surface property of the faces, or empty for the default one.</summary>
    public string SurfaceProperty { get; }

    private const string ConditionallySolid = "conditionallysolid";

    private enum CollisionEffect
    {
        None,
        MakesNonSolid,
        MakesSolid,
        OnlyDrawnWithCollision,
    }

    // In the order they are applied, so water keeps nonsolid faces solid. Half-Life: Alyx and the games of its
    // time also read infectedladder and otherclip, which later games leave out.
    private static readonly (string Attribute, string CollisionGroup, string InteractAs, CollisionEffect Effect)[] CollisionAttributes =
    [
        ("mapbuilder.nonsolid", string.Empty, string.Empty, CollisionEffect.MakesNonSolid),
        ("mapbuilder.ladder", string.Empty, "ladder", CollisionEffect.None),
        ("mapbuilder.infectedladder", string.Empty, "ClimbVersus", CollisionEffect.None),
        ("mapbuilder.blocklos", ConditionallySolid, "blocklos", CollisionEffect.None),
        ("mapbuilder.blocksound", ConditionallySolid, "blocksound", CollisionEffect.None),
        ("mapbuilder.passbullets", ConditionallySolid, "passbullets", CollisionEffect.None),
        ("mapbuilder.npcclip", ConditionallySolid, "npcclip", CollisionEffect.None),
        ("mapbuilder.otherclip", ConditionallySolid, string.Empty, CollisionEffect.None),
        ("mapbuilder.playerclip", ConditionallySolid, "playerclip", CollisionEffect.None),
        ("mapbuilder.sky", ConditionallySolid, "sky", CollisionEffect.None),
        ("mapbuilder.water", ConditionallySolid, "water", CollisionEffect.MakesSolid),
        ("mapbuilder.teleportclip", ConditionallySolid, "teleportclip", CollisionEffect.None),
        ("mapbuilder.navclip", ConditionallySolid, "navclip", CollisionEffect.None),
        ("translucent", ConditionallySolid, "window", CollisionEffect.OnlyDrawnWithCollision),
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="MaterialCollision"/> class with the collision faces with a
    /// material compile to.
    /// </summary>
    /// <param name="material">The material of the faces.</param>
    public MaterialCollision(Material material)
        : this((material ?? throw new ArgumentNullException(nameof(material))).IntAttributes, material.StringAttributes)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MaterialCollision"/> class with the collision faces with a
    /// material that has these attributes compile to.
    /// </summary>
    /// <param name="intAttributes">The integer attributes of the material, see <see cref="Material.IntAttributes"/>.</param>
    /// <param name="stringAttributes">The string attributes of the material, see <see cref="Material.StringAttributes"/>.</param>
    public MaterialCollision(IReadOnlyDictionary<string, long> intAttributes, IReadOnlyDictionary<string, string> stringAttributes)
    {
        ArgumentNullException.ThrowIfNull(intAttributes);
        ArgumentNullException.ThrowIfNull(stringAttributes);

        var isDrawnWithCollision = intAttributes.GetValueOrDefault("mapbuilder.nodraw") == 0 && intAttributes.GetValueOrDefault("nocollision") == 0;
        var isSolid = true;
        var isAlwaysSolid = false;
        var collisionGroup = string.Empty;
        var interactAs = new List<string>();
        var interactWith = new List<string>();
        var interactExclude = new List<string>();

        foreach (var (attribute, group, layer, effect) in CollisionAttributes)
        {
            if (intAttributes.GetValueOrDefault(attribute) == 0
                || (effect == CollisionEffect.OnlyDrawnWithCollision && !isDrawnWithCollision))
            {
                continue;
            }

            if (effect == CollisionEffect.MakesNonSolid)
            {
                isSolid = isAlwaysSolid;
            }
            else if (effect == CollisionEffect.MakesSolid)
            {
                isSolid = true;
                isAlwaysSolid = true;
            }

            if (group.Length > 0)
            {
                collisionGroup = group;
            }

            if (layer.Length > 0)
            {
                interactAs.Add(layer);
            }
        }

        // A collision property no game declares adds nothing
        if (stringAttributes.GetValueOrDefault("mapbuilder.collisionproperties") is { Length: > 0 } name
            && CollisionProperties.Find(name) is { } property)
        {
            isSolid = true;
            collisionGroup = property.CollisionGroup;
            interactAs.AddRange(property.InteractAs);
            interactWith.AddRange(property.InteractWith);
            interactExclude.AddRange(property.InteractExclude);
        }

        // Translucent water is water, not a window
        foreach (var layer in interactAs)
        {
            if (layer.Equals("water", StringComparison.OrdinalIgnoreCase))
            {
                interactAs.Remove("window");
                break;
            }
        }

        IsSolid = isSolid;
        CollisionGroup = collisionGroup;
        InteractAs = interactAs;
        InteractWith = interactWith;
        InteractExclude = interactExclude;
        SurfaceProperty = stringAttributes.GetValueOrDefault("PhysicsSurfaceProperties") ?? string.Empty;
    }

    /// <summary>
    /// Gets whether the faces compile to collision with the given collision attributes. The layers of a material
    /// only count when it gives a collision group, and faces in none are in the default one.
    /// </summary>
    /// <param name="collisionAttributes">Collision attributes of compiled physics, see <see cref="PhysAggregateData.CollisionAttributes"/>.</param>
    /// <returns>Whether they match.</returns>
    public bool CompilesTo(KVObject collisionAttributes)
    {
        ArgumentNullException.ThrowIfNull(collisionAttributes);

        if (!IsSolid)
        {
            return false;
        }

        return CollisionGroup.Length > 0
            ? CollisionProperties.CompilesTo(collisionAttributes, CollisionGroup, InteractAs, InteractWith, InteractExclude)
            : CollisionProperties.CompilesTo(collisionAttributes, "default", [], [], []);
    }

    /// <summary>
    /// Splits a list of interaction layers as maps write them in keys, separated by commas or whitespace.
    /// </summary>
    /// <param name="list">The list, or <see langword="null"/> for none.</param>
    /// <returns>The layers of the list.</returns>
    public static string[] SplitLayers(string? list)
        => (list ?? string.Empty).Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
