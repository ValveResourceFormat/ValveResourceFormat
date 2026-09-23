using ValvePak;
using ValveResourceFormat.Renderer.Entities;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// A map loaded into another one: its 3D sky, or a stage the map loads and unloads as it plays.
/// </summary>
/// <remarks>
/// Each group has a scene of its own, because the baked lighting and visibility of a map only make sense
/// for the geometry they were compiled with. Its entities join the one entity world every group shares.
/// Which view draws it follows from its <see cref="WorldGroup"/>, as in the engine.
/// </remarks>
public sealed class SpawnGroup
{
    /// <summary>Gets the map this group loaded, e.g. <c>maps/prefabs/de_dust2/de_dust2_skybox</c>.</summary>
    public string MapName { get; }

    /// <summary>Gets the scene the group's nodes were loaded into.</summary>
    public Scene Scene { get; }

    /// <summary>Gets where the map is placed: everything it loaded is already in this space.</summary>
    public Matrix4x4 Transform { get; }

    /// <summary>Gets the keyvalues of the entities the group spawned.</summary>
    public IReadOnlyList<Entity> Entities { get; }

    /// <summary>
    /// Gets the world group the group joined, such as a 3D sky's <c>skyboxWorldGroup0</c>, which the sky
    /// view draws. <see langword="null"/> for the map's own, which the main view draws.
    /// </summary>
    public string? WorldGroup => Scene.WorldGroup;

    /// <summary>Gets the entity that loaded the group, such as its <c>skybox_reference</c>.</summary>
    public BaseEntity? PlacedBy { get; init; }

    /// <summary>Gets the map package mounted for the group, which goes when it does.</summary>
    internal Package? MountedPackage { get; init; }

    /// <summary>Initializes a spawn group from a map already loaded into <paramref name="scene"/>.</summary>
    /// <param name="mapName">The map the group loaded.</param>
    /// <param name="scene">The scene it was loaded into.</param>
    /// <param name="transform">Where the map was placed.</param>
    /// <param name="entities">The keyvalues of the entities it spawned.</param>
    public SpawnGroup(string mapName, Scene scene, Matrix4x4 transform, IReadOnlyList<Entity> entities)
    {
        ArgumentNullException.ThrowIfNull(mapName);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(entities);

        MapName = mapName;
        Scene = scene;
        Transform = transform;
        Entities = entities;
    }

    /// <summary>Maps the authored origin of one of <see cref="Entities"/> to where the viewer sees it.</summary>
    public Vector3 EntityOriginToWorld(Vector3 origin)
        => Vector3.Transform(Vector3.Transform(origin, Transform), Scene.ToViewerWorld);
}
