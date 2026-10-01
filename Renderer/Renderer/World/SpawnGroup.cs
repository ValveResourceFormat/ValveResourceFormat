using ValvePak;
using ValveResourceFormat.Renderer.Entities;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// A map loaded into another map, such as a 3D sky or a stage loaded at runtime.
/// </summary>
/// <remarks>
/// Each group gets a separate scene because baked lighting and visibility only apply to the geometry
/// they were compiled with.
/// </remarks>
public sealed class SpawnGroup
{
    /// <summary>Gets the loaded map, e.g. <c>maps/prefabs/de_dust2/de_dust2_skybox</c>.</summary>
    public string MapName { get; }

    /// <summary>Gets the scene holding the loaded nodes.</summary>
    public Scene Scene { get; }

    /// <summary>Gets the placement of the map, already applied to everything it loaded.</summary>
    public Matrix4x4 Transform { get; }

    /// <summary>Gets the keyvalues of the spawned entities.</summary>
    public IReadOnlyList<Entity> Entities { get; }

    /// <summary>Gets the world group, such as <c>skyboxWorldGroup0</c> for a 3D sky, or <see langword="null"/> for the main world.</summary>
    public string? WorldGroup => Scene.WorldGroup;

    /// <summary>Gets the entity that loaded the group, such as a <c>skybox_reference</c>.</summary>
    public BaseEntity? PlacedBy { get; init; }

    internal Package? MountedPackage { get; init; }

    /// <summary>Initializes a new instance of the <see cref="SpawnGroup"/> class.</summary>
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

    /// <summary>Maps an authored entity origin to viewer world space.</summary>
    public Vector3 EntityOriginToWorld(Vector3 origin)
        => Vector3.Transform(Vector3.Transform(origin, Transform), Scene.ToViewerWorld);
}
