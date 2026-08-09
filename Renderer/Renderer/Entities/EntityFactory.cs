using System.Diagnostics.CodeAnalysis;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Constructs an entity of one classname.
/// </summary>
/// <param name="system">The world the entity is being created in.</param>
/// <param name="spawnInfo">The entity's keyvalues and spawn context.</param>
/// <returns>The constructed entity, before <see cref="BaseEntity.Spawn"/> has run.</returns>
public delegate BaseEntity EntityCreator(EntitySystem system, EntitySpawnInfo spawnInfo);

/// <summary>
/// Turns a classname into a live entity. Source's entity factory dictionary: every simulated classname
/// registers here, and anything absent from the table is not one the entity system implements.
/// </summary>
/// <remarks>
/// The table is populated statically rather than by scanning types, so the renderer stays trim-safe and
/// AOT-compatible. Add a classname here to make <see cref="World.WorldLoader"/> hand it over to
/// <see cref="EntitySystem"/> instead of loading it as a static scene node.
/// </remarks>
public static class EntityFactory
{
    private static readonly Dictionary<string, EntityCreator> Creators = new(StringComparer.OrdinalIgnoreCase);

    static EntityFactory()
    {
        Register<FuncRotating>("func_rotating", static (system, spawnInfo) => new FuncRotating(system, spawnInfo));
        Register<TriggerTeleport>("trigger_teleport", static (system, spawnInfo) => new TriggerTeleport(system, spawnInfo));
    }

    /// <summary>Gets the classnames the entity system implements.</summary>
    public static IReadOnlyCollection<string> RegisteredClassnames => Creators.Keys;

    /// <summary>
    /// Whether this classname is simulated by the entity system.
    /// </summary>
    /// <param name="classname">The classname to look up.</param>
    public static bool IsRegistered(string classname) => Creators.ContainsKey(classname);

    /// <summary>
    /// Registers a classname the entity system should simulate, and builds the entity class's table of
    /// <see cref="EntityInputAttribute"/> handlers. Not thread safe: call it during startup, before any
    /// map is loaded.
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
    /// <param name="system">The world the entity is being created in.</param>
    /// <param name="spawnInfo">The entity's keyvalues and spawn context.</param>
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
