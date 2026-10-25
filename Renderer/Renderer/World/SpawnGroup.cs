using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.ResourceTypes;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// A set of entities spawned together, which unload together. Every load makes one: the map itself, each
/// map it places such as a 3D sky or prefab, each <c>info_world_layer</c> that spawns, each
/// <c>info_spawngroup_load_unload</c> load and each <c>point_template</c> instance.
/// </summary>
/// <remarks>
/// <para>
/// Whoever loads a group only describes it: the world it spawns from, the entity lump, the placement, the name
/// fixups and the owner. <see cref="SpawnGroupManager.LoadSpawnGroup"/> loads it, and
/// <see cref="SpawnGroupManager.SynchronouslySpawnGroup"/> spawns it there and then, or the next tick spawns it.
/// </para>
/// <para>
/// A group's entities are created first, then the groups they place finish spawning, then the group spawns and
/// activates its own entities as one batch. Unloading a group unloads its child groups first, then removes the
/// entities it owns. Its entities join <see cref="Owner"/> instead when it has one, and once spawned it hands its
/// child groups to the owner and unloads, leaving nothing behind.
/// </para>
/// <para>
/// The manager gives a group its world before any of its entities spawn. A group that names an entity lump
/// spawns it from the loaded world of that name. One that spawns a whole map loads its own copy of the map, placed
/// where the group is. Only a group drawn as a scene of its own, such as a 3D sky, unloads its geometry and
/// collision. One loaded into the scene of the map that placed it, such as a prefab, leaves them in that scene,
/// where spawning the map at the same place again finds them.
/// </para>
/// </remarks>
public sealed class SpawnGroup
{
    /// <summary>Gets the world the group spawns from, e.g. <c>maps/prefabs/de_dust2/de_dust2_skybox</c>.</summary>
    public string MapName { get; }

    /// <summary>
    /// Gets the entity lump of the world the group spawns, such as a template's or a layer's, or
    /// <see langword="null"/> for the lumps the world spawns when it loads.
    /// </summary>
    public string? EntityLumpName { get; init; }

    /// <summary>Gets what loaded the group, such as <c>point_template</c> or <c>world_layer</c>, or empty for a map.</summary>
    public string DescriptiveName { get; init; } = string.Empty;

    /// <summary>Gets the scene holding the group's nodes, shared with the map that placed it unless it was given one of its own.</summary>
    public Scene Scene { get; }

    /// <summary>Gets the placement of the group, already applied to everything it loaded.</summary>
    public Matrix4x4 Transform { get; }

    /// <summary>Gets what the group puts in place of the markers in its entities' names.</summary>
    public EntityNameFixup NameFixup { get; init; } = EntityNameFixup.None;

    /// <summary>Gets the keyvalues of the entities the group spawns, once it has spawned.</summary>
    public IReadOnlyList<Entity> Entities { get; internal set; } = [];

    /// <summary>Gets the world group, such as <c>skyboxWorldGroup0</c> for a 3D sky, or <see langword="null"/> for the main world.</summary>
    public string? WorldGroup => Scene.WorldGroup;

    /// <summary>Gets the group the entities join instead of this one, or <see langword="null"/> when they join this one.</summary>
    public SpawnGroup? Owner { get; init; }

    /// <summary>Gets whether the group is loaded, from <see cref="SpawnGroupManager.LoadSpawnGroup"/> until it unloads.</summary>
    public bool IsLoaded { get; internal set; }

    /// <summary>Gets whether the group's entities have spawned.</summary>
    public bool IsSpawned { get; internal set; }

    /// <summary>
    /// Gets whether <see cref="Scene"/> belongs to this group alone, drawn once the group spawns and released when
    /// it unloads.
    /// </summary>
    public bool HasOwnScene { get; internal init; }

    // The lump found by whoever loaded the group, spawned instead of looking EntityLumpName up again
    internal EntityLump? EntityLump { get; init; }

    // The visibility layer every entity goes on, instead of the one each would go on
    internal string? LayerName { get; init; }

    // The world the group spawns from, given by the manager when it spawns, or by the WorldLoader of a map
    // spawning its own entities
    internal WorldLoader? World { get; set; }

    internal string? MountedMapPackage { get; set; }

    internal List<Action<IReadOnlyList<BaseEntity>>> FinishListeners { get; } = [];

    // The groups added to this one as children, which unload with it. One unloaded on its own stays listed.
    internal List<SpawnGroup> Children { get; } = [];

    /// <summary>Initializes a new instance of the <see cref="SpawnGroup"/> class.</summary>
    public SpawnGroup(string mapName, Scene scene, Matrix4x4 transform)
    {
        ArgumentNullException.ThrowIfNull(mapName);
        ArgumentNullException.ThrowIfNull(scene);

        MapName = mapName;
        Scene = scene;
        Transform = transform;
    }

    /// <summary>Maps an authored entity origin to viewer world space.</summary>
    public Vector3 EntityOriginToWorld(Vector3 origin)
        => Vector3.Transform(Vector3.Transform(origin, Transform), Scene.ToViewerWorld);
}
