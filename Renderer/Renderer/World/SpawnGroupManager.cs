using System.IO;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// Loads, spawns and unloads the <see cref="SpawnGroup"/>s of an <see cref="EntitySystem"/>, and tells their
/// listeners about it.
/// </summary>
/// <remarks>
/// <para>
/// Spawn groups are not part of the entity system: the engine loads them, and a server game system keeps what
/// the game needs of each. The entity system only creates and spawns their entities and holds the active group.
/// </para>
/// <para>
/// Nothing that draws knows spawn groups either. A group with a scene of its own hands it to the
/// <see cref="Host"/> once it spawns, which draws it with the other scenes of its world group, and takes it
/// back when it unloads.
/// </para>
/// </remarks>
public sealed class SpawnGroupManager
{
    private readonly EntitySystem entitySystem;

    private readonly List<SpawnGroup> groups = [];

    // Loaded without being told to spawn there and then, which the next tick spawns
    private readonly List<SpawnGroup> pendingGroups = [];

    // Maps loaded into the scene of the map that placed them, whose geometry and collision stay in that scene
    // after their group unloads
    private readonly List<WorldLoader> placedWorlds = [];

    // Never reused while the game runs
    private int nameFixupCount;
    private int templateInstanceCount;

    internal SpawnGroupManager(EntitySystem entitySystem)
    {
        this.entitySystem = entitySystem;
    }

    /// <summary>Gets or sets the host that draws the scenes of the groups with a scene of their own.</summary>
    public ISceneHost? Host { get; set; }

    /// <summary>Gets the loaded spawn groups, in load order.</summary>
    public IReadOnlyList<SpawnGroup> Groups => groups;

    /// <summary>Finds a spawned group by the world it spawns from, comparing the names as paths.</summary>
    /// <param name="worldName">The world, such as <c>maps/stages/lms_stage1</c>.</param>
    /// <returns>The first group loaded from it, or <see langword="null"/>.</returns>
    public SpawnGroup? FindSpawnGroupByWorldName(string worldName)
        => groups.Find(group => group.IsSpawned && group.MapName.Equals(worldName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Loads a spawn group as its loader described it. Nothing of it spawns until
    /// <see cref="SynchronouslySpawnGroup"/> is called, or the next tick otherwise.
    /// </summary>
    /// <param name="group">The group to load, which loads only once.</param>
    public void LoadSpawnGroup(SpawnGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (group.IsLoaded || group.IsSpawned)
        {
            throw new InvalidOperationException($"Spawn group '{group.MapName}' has been loaded already.");
        }

        group.IsLoaded = true;
        groups.Add(group);
        pendingGroups.Add(group);
    }

    /// <summary>Makes a loaded spawn group a child of another, which unloads it along with itself.</summary>
    /// <param name="parent">The group to unload the child with.</param>
    /// <param name="child">The group to unload with the parent.</param>
    public void AddChildSpawnGroup(SpawnGroup parent, SpawnGroup child)
    {
        ThrowIfNotLoaded(parent);
        ThrowIfNotLoaded(child);

        parent.Children.Add(child);
    }

    /// <summary>Calls back once a loaded spawn group has spawned, with the entities it spawned.</summary>
    /// <param name="group">The group to wait for.</param>
    /// <param name="onFinished">Called with the entities spawned, in the order the listeners were added.</param>
    public void ListenForSpawnGroupToFinish(SpawnGroup group, Action<IReadOnlyList<BaseEntity>> onFinished)
    {
        ThrowIfNotLoaded(group);
        ArgumentNullException.ThrowIfNull(onFinished);

        group.FinishListeners.Add(onFinished);
    }

    private void ThrowIfNotLoaded(SpawnGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (!group.IsLoaded || !groups.Contains(group))
        {
            throw new InvalidOperationException($"Spawn group '{group.MapName}' is not loaded into this entity system.");
        }
    }

    /// <summary>
    /// Spawns a loaded spawn group there and then, rather than on the next tick. The group gets its world first,
    /// see <see cref="AcquireWorld"/>, then its entities spawn from it.
    /// </summary>
    /// <param name="group">The group to spawn. Nothing happens if it is not loaded or has spawned.</param>
    public void SynchronouslySpawnGroup(SpawnGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (!group.IsLoaded || group.IsSpawned)
        {
            return;
        }

        group.IsSpawned = true;
        pendingGroups.Remove(group);

        group.World ??= AcquireWorld(group);

        var spawned = group.World is { } world ? SpawnGroupLoader.Spawn(entitySystem, group, world) : [];

        FinishSpawnGroup(group, spawned);

        if (group.HasOwnScene && group.World != null)
        {
            Host?.AddScene(group.Scene);
        }

        if (group.Owner is { } owner)
        {
            MergeIntoOwner(group, owner);
        }
    }

    /// <summary>
    /// Gives a group the world it spawns from, the one place that decides whether a world loads. As in the engine,
    /// a group naming an entity lump shares the loaded world of that name in its world group, and one spawning a
    /// whole map loads a world of its own.
    /// </summary>
    /// <remarks>
    /// The engine loads the world of a lump group when none is loaded; the viewer only spawns lumps of worlds it
    /// shows. A whole map that an earlier group left in the same scene at the same place is spawned from again
    /// instead of loading twice, as when the entities of the map spawn again. A world loaded here has its lighting;
    /// its meshes and collision follow its entities, see <see cref="WorldLoader.LoadAfterEntities"/>.
    /// </remarks>
    /// <returns>The world, or <see langword="null"/> when there is none to spawn from.</returns>
    private WorldLoader? AcquireWorld(SpawnGroup group)
    {
        if (group.EntityLumpName != null)
        {
            var loaded = FindLoadedWorld(group.WorldGroup, group.MapName);

            if (loaded == null)
            {
                entitySystem.Logger.LogWarning("Spawn group '{DescriptiveName}' found no loaded world named '{MapName}'", group.DescriptiveName, group.MapName);
            }

            return loaded;
        }

        if (placedWorlds.Find(world => world.Scene == group.Scene && world.RootTransform == group.Transform
            && world.MapName.Equals(group.MapName, StringComparison.OrdinalIgnoreCase)) is { } placed)
        {
            return placed;
        }

        var loadedWorld = LoadWorld(group);

        if (loadedWorld is { OwnsScene: false })
        {
            placedWorlds.Add(loadedWorld);
        }

        return loadedWorld;
    }

    // Loads the map of a group placed where the group is. A group with a scene of its own keeps the map package
    // mounted until it unloads, even when the map failed to load.
    private WorldLoader? LoadWorld(SpawnGroup group)
    {
        var fileLoader = entitySystem.RendererContext.FileLoader;

        if (group.MountedMapPackage == null)
        {
            var mapPackage = $"{group.MapName}.vpk";

            if (!fileLoader.TryMountMapPackage(mapPackage))
            {
                return null;
            }

            group.MountedMapPackage = mapPackage;
        }

        WorldLoader? world = null;

        try
        {
            world = WorldLoader.LoadSpawnGroupWorld($"{group.MapName}.vmap", group.Scene, entitySystem, group.Transform, ownsScene: group.HasOwnScene);
        }
        catch (FileNotFoundException e)
        {
            entitySystem.Logger.LogWarning("Not loading '{MapName}': {Message}", group.MapName, e.Message);
        }

        // A prefab joins the scene of the map that placed it, which needs nothing of its package once loaded
        if (!group.HasOwnScene)
        {
            fileLoader.UnmountMapPackage(group.MountedMapPackage);
            group.MountedMapPackage = null;
        }

        return world;
    }

    // A group whose entities joined an owner hands it its child groups once it has spawned, and unloads, as
    // nothing is left in it. Without this every point_template instance would stay loaded with the world.
    private void MergeIntoOwner(SpawnGroup group, SpawnGroup owner)
    {
        owner.Children.AddRange(group.Children);
        group.Children.Clear();

        UnloadSpawnGroup(group);
    }

    // Called once a group's entities have spawned and activated
    private static void FinishSpawnGroup(SpawnGroup group, IReadOnlyList<BaseEntity> spawned)
    {
        // A listener may load groups of its own, which listen for themselves
        var listeners = group.FinishListeners.ToArray();
        group.FinishListeners.Clear();

        foreach (var listener in listeners)
        {
            listener(spawned);
        }
    }

    // A group loaded asynchronously spawns on the tick after it loads
    internal void SpawnPendingGroups()
    {
        if (pendingGroups.Count == 0)
        {
            return;
        }

        foreach (var group in pendingGroups.ToArray())
        {
            SynchronouslySpawnGroup(group);
        }
    }

    /// <summary>
    /// Makes a loaded spawn group the active one, which a <c>point_template</c> set to insert into it gives its
    /// entities to.
    /// </summary>
    /// <param name="group">The group to activate.</param>
    public void PerformActivateSpawnGroup(SpawnGroup group)
    {
        ThrowIfNotLoaded(group);

        entitySystem.ActiveSpawnGroup = group;
    }

    /// <summary>
    /// Unloads a spawn group: its children first, then every entity it owns, then its scene and collision when
    /// it has a scene of its own.
    /// </summary>
    /// <param name="group">The group to unload. Nothing happens if it is not loaded.</param>
    public void UnloadSpawnGroup(SpawnGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (!group.IsLoaded)
        {
            return;
        }

        group.IsLoaded = false;
        group.FinishListeners.Clear();
        pendingGroups.Remove(group);

        for (var i = group.Children.Count - 1; i >= 0; i--)
        {
            UnloadSpawnGroup(group.Children[i]);
        }

        group.Children.Clear();

        // The entities of a group with an owner belong to the owner
        if (group.Owner == null)
        {
            var entities = entitySystem.Entities;

            for (var i = 0; i < entities.Count; i++)
            {
                if (entities[i].SpawnGroup == group)
                {
                    entitySystem.Remove(entities[i]);
                }
            }
        }

        groups.Remove(group);

        // The engine keeps a stale handle instead
        if (entitySystem.ActiveSpawnGroup == group)
        {
            entitySystem.ActiveSpawnGroup = null;
        }

        ReleaseOwnScene(group);
    }

    // Takes back the scene a group handed to the host when it spawned, or disposes the one it never handed over
    // as it failed to spawn, and the map package mounted for it
    private void ReleaseOwnScene(SpawnGroup group)
    {
        if (!group.HasOwnScene)
        {
            return;
        }

        // The prefabs it placed go with its scene
        placedWorlds.RemoveAll(world => world.Scene == group.Scene);

        if (group.World != null)
        {
            // Only the group's own collision; the world group keeps its physics world
            entitySystem.GetPhysicsWorld(group.WorldGroup).Remove(group.Scene);
            Host?.RemoveScene(group.Scene);
        }
        else
        {
            group.Scene.Dispose();
        }

        if (group.MountedMapPackage is { } mapPackage)
        {
            entitySystem.RendererContext.FileLoader.UnmountMapPackage(mapPackage);
            group.MountedMapPackage = null;
        }
    }

    // Forgets every group once their entities are gone, releasing the scenes of their own
    internal void Clear()
    {
        foreach (var group in groups)
        {
            group.IsLoaded = false;
            ReleaseOwnScene(group);
        }

        groups.Clear();
        pendingGroups.Clear();
        nameFixupCount = 0;
        templateInstanceCount = 0;
    }

    // Forgets the maps left in scenes once the scenes are emptied and the collision is dropped
    internal void ForgetPlacedWorlds() => placedWorlds.Clear();

    // Number for the next placed map that gives its entity names a prefix of its own
    internal int NextNameFixupIndex() => ++nameFixupCount;

    // Number for the next point_template instance that gives its entity names a suffix of its own. The engine
    // never resets its counter; this one restarts with the world, so the names repeat when it spawns again.
    internal int NextTemplateInstanceIndex() => ++templateInstanceCount;

    // A map placed more than once in a world group is found as the first one loaded
    internal WorldLoader? FindLoadedWorld(string? worldGroup, string mapName)
        => FindLoadedWorld(world => world.Scene.WorldGroup == worldGroup && world.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase));

    // A template's world is found by name alone, in any world group
    internal EntityLump? GetEntityLumpForTemplate(string worldName, string lumpName)
        => FindLoadedWorld(world => world.MapName.Equals(worldName, StringComparison.OrdinalIgnoreCase))?.FindEntityLump(lumpName);

    // A loaded world is one a group that spawns a whole map loaded, rather than a lump of it
    private WorldLoader? FindLoadedWorld(Predicate<WorldLoader> match)
    {
        foreach (var group in groups)
        {
            if (group is { EntityLumpName: null, World: { } world } && match(world))
            {
                return world;
            }
        }

        return null;
    }
}
