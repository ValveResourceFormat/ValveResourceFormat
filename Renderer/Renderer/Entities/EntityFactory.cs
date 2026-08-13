using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Constructs an entity of one classname.
/// </summary>
/// <returns>The constructed entity, before <see cref="BaseEntity.Spawn"/> has run.</returns>
public delegate BaseEntity EntityCreator(EntitySystem system, EntitySpawnInfo spawnInfo);

/// <summary>
/// Turns a classname into a live entity. Source's entity factory dictionary: every simulated classname
/// registers here, and anything absent from the table is not one the entity system implements.
/// </summary>
/// <remarks>
/// The table is filled in statically rather than by scanning types, to stay trim-safe and AOT-compatible.
/// Add a classname here and <see cref="World.WorldLoader"/> hands it to <see cref="EntitySystem"/> instead
/// of loading it as a static scene node.
/// </remarks>
public static class EntityFactory
{
    // Concurrent for the same reason the input tables are: the registrations themselves all happen in the
    // static constructor, but the map loads that read this run on several threads at once, and nothing in
    // the type stops a caller registering a classname of its own later
    private static readonly ConcurrentDictionary<string, EntityCreator> Creators = new(StringComparer.OrdinalIgnoreCase);

    static EntityFactory()
    {
        Register<FuncRotating>("func_rotating", static (system, spawnInfo) => new FuncRotating(system, spawnInfo));
        Register<TriggerTeleport>("trigger_teleport", static (system, spawnInfo) => new TriggerTeleport(system, spawnInfo));
    }

    /// <summary>
    /// Whether this classname is simulated by the entity system.
    /// </summary>
    public static bool IsRegistered(string classname) => Creators.ContainsKey(classname);

    /// <summary>
    /// Registers a classname the entity system should simulate, and builds the entity class's table of
    /// <see cref="EntityInputAttribute"/> handlers. Safe to call while maps are loading, though the static
    /// constructor below is where the entity system's own classnames are declared.
    /// </summary>
    /// <typeparam name="T">The entity class the classname spawns.</typeparam>
    /// <param name="classname">The classname to link, matched case-insensitively.</param>
    /// <param name="creator">Constructs the entity.</param>
    public static void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] T>(
        string classname, EntityCreator creator)
        where T : BaseEntity
    {
        Creators[classname] = creator;
        EntityInputTable.Bind<T>();
    }

    /// <summary>
    /// Creates and spawns the entity for a classname. The entity is fully set up when this returns, but
    /// is not in the world yet; <see cref="EntitySystem.CreateEntity"/> is what puts it there.
    /// </summary>
    /// <returns>The spawned entity, or <see langword="null"/> when the classname is not implemented.</returns>
    public static BaseEntity? Create(EntitySystem system, EntitySpawnInfo spawnInfo)
    {
        var classname = spawnInfo.Data.GetStringProperty("classname");

        if (classname == null || !Creators.TryGetValue(classname, out var creator))
        {
            return null;
        }

        var entity = creator(system, spawnInfo);
        entity.Spawn();

        return entity;
    }
}
