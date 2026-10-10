using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using Entity = ValveResourceFormat.ResourceTypes.EntityLump.Entity;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// What an authored entity I/O connection addresses: a name and how to read it.
/// </summary>
/// <param name="Name">
/// The target name. May hold <c>*</c> and <c>?</c> wildcards, or be a <c>!</c> name that stands
/// for an entity in the firing chain.
/// </param>
/// <param name="Type">Whether the name is a targetname or a classname.</param>
public readonly record struct EntityIOTarget(string Name, EntityIOTargetType Type);

/// <summary>
/// Owns the living entities, runs them on a fixed tick, and carries the entity I/O queue.
/// </summary>
/// <remarks>
/// Entities tick at <see cref="TickInterval"/>, not once per frame. A frame runs the ticks it owes, up to
/// <see cref="MaxTicksPerFrame"/>, so a hitch cannot make simulating take longer than drawing.
/// </remarks>
public sealed class EntitySystem
{
    /// <summary>The fixed simulation tick, 64 per second.</summary>
    public const float TickInterval = 1f / 64f;

    // Unlike in game, where a zero-delay cycle hangs, a viewer must survive one in someone else's map.
    // Far above real wiring (a large jailbreak map has under two thousand connections in total and a
    // tick delivers a few dozen), so hitting it means a cycle.
    private const int MaxInputsPerTick = 100_000;

    /// <summary>The most ticks one frame may run before the leftover time is dropped.</summary>
    public const int MaxTicksPerFrame = 8;

    /// <summary>Gets the shared renderer context this world loads and logs through.</summary>
    public RendererContext RendererContext { get; }

    /// <summary>
    /// Gets the physics world of the main world group, which every trace is made against. It holds the
    /// world physics of every spawn group loaded into that group.
    /// </summary>
    public PhysicsWorld PhysicsWorld { get; } = new();

    // Other world groups, such as a 3D sky, have a physics world of their own that nothing traces
    private readonly Dictionary<string, PhysicsWorld> worldGroupPhysicsWorlds = [];

    /// <summary>Gets the physics world of a world group, creating it on first use.</summary>
    /// <param name="worldGroup">The world group, or <see langword="null"/> for the main one.</param>
    public PhysicsWorld GetPhysicsWorld(string? worldGroup)
    {
        if (worldGroup == null)
        {
            return PhysicsWorld;
        }

        if (!worldGroupPhysicsWorlds.TryGetValue(worldGroup, out var physicsWorld))
        {
            physicsWorld = new PhysicsWorld();
            worldGroupPhysicsWorlds.Add(worldGroup, physicsWorld);
        }

        return physicsWorld;
    }

    /// <summary>Gets the loader for entity models and physics.</summary>
    public IFileLoader FileLoader => RendererContext.FileLoader;

    /// <summary>Gets the logger for entity problems.</summary>
    public ILogger Logger => RendererContext.Logger;

    /// <summary>Gets every living entity, in spawn order.</summary>
    public IReadOnlyList<BaseEntity> Entities => entities;

    /// <summary>
    /// Gets or sets whether the world is simulated. When off the ticks stop, so nothing thinks, moves,
    /// touches or fires entity I/O. Spawned entities stay in the scene and stay drawn.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets the player, once one has been spawned into this world.</summary>
    public PlayerEntity? Player { get; private set; }

    /// <summary>
    /// Gets the world entity at the root of the hierarchy, once a map has loaded. A trace that hits the
    /// static world reports it as the entity hit.
    /// </summary>
    public WorldEntity? World { get; private set; }

    /// <summary>Gets the current simulation time in seconds.</summary>
    public float CurrentTime { get; private set; }

    /// <summary>Rounds a time onto the nearest tick.</summary>
    /// <remarks>
    /// Rounding up to the first tick at or after the time would make a repeating 0.1s think run every 7
    /// ticks instead of 6, about a sixth slow.
    /// </remarks>
    /// <returns>The time of the nearest tick.</returns>
    public static float SnapToTick(float time) => (int)(0.5f + (time / TickInterval)) * TickInterval;

    /// <summary>Gets the number of ticks simulated so far.</summary>
    public int TickCount { get; private set; }

    /// <summary>
    /// Gets how far the current frame sits between the last two simulated ticks, in [0, 1). Entities
    /// interpolate their render transform across it so movement is smooth at any framerate.
    /// </summary>
    public float InterpolationFraction => tickAccumulator / TickInterval;

    // Walked by index: thinks, inputs and touch handlers may spawn or remove entities mid-walk, and one
    // spawned mid-walk is meant to be reached
    private readonly List<BaseEntity> entities = [];
    private readonly List<BaseEntity> parented = [];

    private readonly Dictionary<string, List<BaseEntity>> entitiesByName = [];
    private static readonly List<BaseEntity> NoEntities = [];

    private readonly List<QueuedInput> inputQueue = [];
    private readonly Dictionary<EntityLump.Connection, int> firedCounts = [];
    private readonly HashSet<BaseEntity> playerImpacts = [];
    private long sequence;
    private float tickAccumulator;
    private bool hasRemovedEntities;

    /// <summary>
    /// Initializes an entity world. Prefer <see cref="Renderer.EntitySystem"/> over constructing one.
    /// </summary>
    /// <param name="context">The shared renderer context entities load and log through.</param>
    public EntitySystem(RendererContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        RendererContext = context;
        TempEntities = new TempEntities(this);
    }

    /// <summary>Gets the one-shot effects that have no entity of their own, such as bullet impacts.</summary>
    public TempEntities TempEntities { get; }

    /// <summary>
    /// Creates the entity for a map entity's keyvalues and puts it in the world. An unimplemented classname
    /// becomes a <see cref="GenericModelEntity"/> or <see cref="GenericEntity"/>, which only draw themselves.
    /// </summary>
    /// <param name="data">The entity's keyvalues.</param>
    /// <param name="parentTransform">Transform of whatever spawned it.</param>
    /// <param name="layerName">Visibility layer for its nodes.</param>
    /// <param name="intoScene">Scene the entity's nodes render into.</param>
    /// <param name="nameFixup">What the spawning group puts in place of the markers in the entity's names.</param>
    /// <returns>
    /// The spawned entity, or <see langword="null"/> if the keyvalues name no classname or a <c>worldspawn</c>.
    /// </returns>
    public BaseEntity? CreateEntity(Entity data, Matrix4x4 parentTransform, string? layerName, Scene intoScene, EntityNameFixup nameFixup)
    {
        var entity = EntityFactory.Create(this, new EntitySpawnInfo(data, parentTransform, layerName, intoScene, nameFixup));

        if (entity == null)
        {
            return null;
        }

        // A child's Spawn must see its pose local to its move parent, which may not exist yet, so it
        // spawns in Activate once bound.
        if (string.IsNullOrEmpty(data.GetStringProperty("parentname")))
        {
            entity.Spawn();
        }
        else
        {
            pendingSpawns.Add(entity);
        }

        Add(entity);

        return entity;
    }

    /// <summary>Puts the player into the world, replacing any already spawned.</summary>
    /// <returns>The player entity.</returns>
    public PlayerEntity SpawnPlayer(IPlayerController controller, Scene scene)
    {
        if (Player != null)
        {
            Remove(Player);
        }

        // Inputs are normally bound at factory registration, which the player skips
        EntityInputTable.Bind<PlayerEntity>();

        Player = new PlayerEntity(this, scene, controller);
        Player.Spawn();
        Add(Player);
        scene.Player = Player;

        return Player;
    }

    private void Add(BaseEntity entity)
    {
        entity.Owner ??= World;

        entities.Add(entity);

        if (!string.IsNullOrEmpty(entity.TargetName))
        {
            var key = NameKey(entity.TargetName);

            if (!entitiesByName.TryGetValue(key, out var named))
            {
                named = [];
                entitiesByName.Add(key, named);
            }

            named.Add(entity);
        }
    }

    // Created on the first map load, before any of its entities so it owns them all. Stays until Clear.
    internal void SpawnWorld(Scene scene)
    {
        if (World != null)
        {
            return;
        }

        World = new WorldEntity(this, scene);
        World.Spawn();
        Add(World);
    }

    /// <summary>Adds an entity built in code rather than from map keyvalues.</summary>
    public void AddEntity(BaseEntity entity) => Add(entity);

    // Never reused while the game runs
    private int nameFixupCount;

    // Number for the next placed map that gives its entity names a prefix of its own
    internal int NextNameFixupIndex() => ++nameFixupCount;

    /// <summary>Gets or sets the host that draws spawn groups loaded at runtime.</summary>
    public ISpawnGroupHost? SpawnGroupHost { get; set; }

    internal void AddSpawnGroup(World.SpawnGroup group)
    {
        Activate();
        SpawnGroupHost?.AddSpawnGroup(group);
    }

    /// <summary>Removes the entities of a spawn group and releases the group.</summary>
    public void RemoveSpawnGroup(World.SpawnGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        for (var i = 0; i < entities.Count; i++)
        {
            if (entities[i].Scene == group.Scene)
            {
                Remove(entities[i]);
            }
        }

        // Only the group's own collision; the world group keeps its physics world
        if (group.WorldGroup == null)
        {
            PhysicsWorld.Remove(group.Scene);
        }
        else if (worldGroupPhysicsWorlds.TryGetValue(group.WorldGroup, out var physicsWorld))
        {
            physicsWorld.Remove(group.Scene);
        }
        SpawnGroupHost?.RemoveSpawnGroup(group);
    }

    /// <summary>
    /// Runs <see cref="BaseEntity.Activate"/> on every entity spawned since the last call. Called as each
    /// spawn group (the map, its 3D skybox) finishes spawning.
    /// </summary>
    public void Activate()
    {
        // Move parents first, so every entity activates seeing the finished hierarchy
        for (var i = activatedCount; i < entities.Count; i++)
        {
            ResolveMoveParentChain(entities[i]);
        }

        // HACK: Parents before their children, so a parent's Spawn has placed it before its children spawn
        var spawning = new HashSet<BaseEntity>(pendingSpawns);

        foreach (var entity in parented)
        {
            if (spawning.Remove(entity))
            {
                entity.Spawn();
            }
        }

        // Named a parent that does not exist
        foreach (var entity in pendingSpawns)
        {
            if (spawning.Remove(entity))
            {
                entity.Spawn();
            }
        }

        pendingSpawns.Clear();

        for (var i = activatedCount; i < entities.Count; i++)
        {
            entities[i].Activate();
        }

        activatedCount = entities.Count;
    }

    // Lists the entity in parented behind its whole parent chain, the order children tick and draw in
    internal void ResolveMoveParentChain(BaseEntity entity)
    {
        if (entity.IsMoveParentResolved)
        {
            return;
        }

        entity.ResolveMoveParent();

        if (entity.MoveParent != null)
        {
            parented.Add(entity);
        }
    }

    private int activatedCount;

    // Parented entities created since the last Activate, waiting for their parent to be bound
    private readonly List<BaseEntity> pendingSpawns = [];

    /// <summary>
    /// Notify every entity a round started.
    /// </summary>
    public void StartRound()
    {
        for (var i = 0; i < entities.Count; i++)
        {
            if (!entities[i].IsRemoved)
            {
                entities[i].RoundStart();
            }
        }
    }

    /// <summary>Removes an entity from the world and its nodes from the scene.</summary>
    public void Remove(BaseEntity entity)
    {
        if (entity.IsRemoved)
        {
            return;
        }

        // Anything it was standing in should hear that it left before it stops existing
        for (var i = 0; i < entities.Count; i++)
        {
            entities[i].UpdateTouchLink(entity, isOverlapping: false);
        }

        entity.RemoveFromScene();
        hasRemovedEntities = true;

        if (Player == entity)
        {
            Player = null;
        }
    }

    // Tests every trigger volume against the player, opening and closing touch links as they change. Both
    // sides of a touch hear about it.
    //
    // Only the player is tested: testing every pair costs a test per trigger per entity per tick, and
    // nothing else here walks into a volume. So an entity the simulation moves into a trigger does not
    // fire it. The touch machinery is general, so this is the one place to widen.
    //
    // Runs on the tick only, because touch handlers teleport things, queue inputs against CurrentTime and
    // spawn or remove entities, which would become framerate-dependent per frame. The player moves per
    // frame, so a touch resolves up to one tick late, reading the player's live position.
    private void UpdateTouchLinks()
    {
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];

            if (!entity.IsTrigger || entity.IsRemoved || entity.Collider is not { IsEmpty: false } volume)
            {
                continue;
            }

            // Re-read per trigger: an earlier trigger in this pass may have teleported the player
            if (Player is not { IsRemoved: false } player
                || !player.TryGetTouchBounds(out var center, out var halfExtents))
            {
                return;
            }

            // Entities of the 3D sky share coordinates with the map but must not touch it
            if (entity.Scene.WorldGroup != player.Scene.WorldGroup)
            {
                continue;
            }

            // Against the volume, not its surface, so a player deep inside a big trigger still touches it.
            // Rejects on world bounds first, so a distant trigger costs one box test.
            var isOverlapping = volume.OverlapsVolume(center, halfExtents);

            entity.UpdateTouchLink(player, isOverlapping);
            player.UpdateTouchLink(entity, isOverlapping);
        }
    }

    /// <summary>Records that the player hit a solid entity, for the next tick to report as a touch.</summary>
    /// <param name="entity">The entity a movement sweep hit.</param>
    public void NotePlayerImpact(BaseEntity entity) => playerImpacts.Add(entity);

    /// <summary>Notes what a sweep of the player ran into, unless it missed or struck the world.</summary>
    public void NotePlayerImpact(in Rubikon.TraceResult trace)
    {
        if (trace.HitEntity is { } entity && entity != World)
        {
            NotePlayerImpact(entity);
        }
    }

    // On the tick rather than as the player moves, for the same reason as the trigger touches
    private void DispatchPlayerImpacts()
    {
        if (playerImpacts.Count == 0)
        {
            return;
        }

        BaseEntity[] impacted = [.. playerImpacts];
        playerImpacts.Clear();

        if (Player is not { IsRemoved: false } player)
        {
            return;
        }

        foreach (var entity in impacted)
        {
            if (!entity.IsRemoved && entity.Scene.WorldGroup == player.Scene.WorldGroup)
            {
                entity.Impact(player);
            }
        }
    }

    /// <summary>
    /// Drops every entity and resets the clock. Scene nodes are cleaned up by
    /// <see cref="Renderer.Clear"/>, which runs first.
    /// </summary>
    public void Clear()
    {
        foreach (var entity in entities)
        {
            if (!entity.IsRemoved)
            {
                entity.RemoveFromScene();
            }
        }

        entities.Clear();
        entitiesByName.Clear();
        parented.Clear();
        pendingSpawns.Clear();
        PhysicsWorld.Clear();
        worldGroupPhysicsWorlds.Clear();
        World = null;
        Player = null;
        activatedCount = 0;
        inputQueue.Clear();
        firedCounts.Clear();
        playerImpacts.Clear();
        TempEntities.Clear();
        hasRemovedEntities = false;
        tickAccumulator = 0f;
        CurrentTime = 0f;
        TickCount = 0;
    }

    /// <summary>Advances the world by a frame's worth of time, running whole ticks.</summary>
    public void Update(float frameTime)
    {
        TempEntities.Update();

        if (entities.Count <= 1)
        {
            return;
        }

        using var profilerScope = new ProfilerScope("Entity System");

        if (Enabled)
        {
            tickAccumulator += frameTime;

            for (var i = 0; tickAccumulator >= TickInterval; i++)
            {
                if (i == MaxTicksPerFrame)
                {
                    // Fell too far behind to catch up; drop the backlog rather than spiral
                    tickAccumulator = 0f;
                    break;
                }

                tickAccumulator -= TickInterval;
                Tick();
            }
        }
        else
        {
            // Paused time is not owed back, and entities rest on their last tick state rather than
            // part way to the next one
            tickAccumulator = 0f;
        }

        // Entities are not scene nodes, so nothing else places what they own. Parents first, so a child
        // follows its parent's current pose.
        foreach (var entity in entities)
        {
            if (entity.MoveParent == null)
            {
                entity.Update();
            }
        }

        foreach (var entity in parented)
        {
            entity.Update();
        }
    }

    private void Tick()
    {
        TickCount++;
        CurrentTime = TickCount * TickInterval;

        foreach (var entity in entities)
        {
            entity.BeginTick();
        }

        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];

            if (!entity.IsRemoved && entity.MoveParent == null)
            {
                entity.Simulate(TickInterval);
            }
        }

        // Children move in their parent frame, so they simulate after the parents they ride
        for (var i = 0; i < parented.Count; i++)
        {
            var entity = parented[i];

            if (!entity.IsRemoved)
            {
                entity.Simulate(TickInterval);
            }
        }

        if (hasRemovedEntities)
        {
            // Activated entities are at the front of the list, so the count drops by each removed one
            for (var i = activatedCount - 1; i >= 0; i--)
            {
                if (entities[i].IsRemoved)
                {
                    activatedCount--;
                }
            }

            entities.RemoveAll(static entity => entity.IsRemoved);
            parented.RemoveAll(static entity => entity.IsRemoved);

            foreach (var named in entitiesByName.Values)
            {
                named.RemoveAll(static entity => entity.IsRemoved);
            }

            hasRemovedEntities = false;
        }

        UpdateTouchLinks();
        DispatchPlayerImpacts();

        // Last, after everything has moved and touched, so a touch handler's outputs land in the tick that
        // saw the touch. A think scheduled for the current time waits for the next tick.
        DispatchDueInputs();
    }

    /// <summary>
    /// Sweeps an axis-aligned box against every solid entity, narrowing <paramref name="result"/> to the
    /// nearest hit.
    /// </summary>
    /// <returns><see langword="true"/> when an entity produced the nearest hit.</returns>
    public bool TraceAABB(Vector3 from, Vector3 to, Vector3 halfExtents, bool detectStartSolid, ref Rubikon.TraceResult result)
    {
        var hitEntity = false;

        // Shrunk so only real penetration counts as inside: resting contact at the keep-away gap,
        // noise included, stays on the ordinary solid path
        var insideExtents = halfExtents - new Vector3(Rubikon.SurfaceEpsilon / 2f);

        foreach (var entity in entities)
        {
            if (!entity.IsCollidable || !entity.Collider!.MightHit(from, to, halfExtents))
            {
                continue;
            }

            // A hull inside this entity cannot be swept (the SAT sweep is meaningless from an overlapping
            // start). A move ending fully outside steps out freely, anything else stops where it stands:
            // escape is always possible, crossing the interior never is, and a pusher never leaves the
            // player deep inside it.
            if (entity.Collider.OverlapsVolume(from, insideExtents))
            {
                if (!entity.Collider.OverlapsVolume(to, insideExtents))
                {
                    continue;
                }

                var move = to - from;
                var normal = MathUtils.SafeNormalize(-move, Vector3.UnitZ, 1e-12f);

                var blocked = new Rubikon.TraceResult(true, from, normal, 0f, -1)
                {
                    StartSolid = detectStartSolid,
                    ContactPoint = from,
                    HitEntity = entity,
                };

                hitEntity |= result.MinimizeWith(blocked);
                continue;
            }

            var entityTrace = entity.Collider.TraceAABB(from, to, halfExtents, detectStartSolid);
            entityTrace.HitEntity = entity;

            hitEntity |= result.MinimizeWith(entityTrace);
        }

        return hitEntity;
    }

    /// <summary>Gets whether an axis-aligned box overlaps any solid entity where it stands.</summary>
    public bool OverlapsSolidEntity(Vector3 center, Vector3 halfExtents)
    {
        foreach (var entity in entities)
        {
            if (entity.IsCollidable && entity.Collider!.OverlapsVolume(center, halfExtents))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Traces a ray against every solid entity, narrowing <paramref name="result"/> to the nearest hit.
    /// </summary>
    /// <returns><see langword="true"/> when an entity produced the nearest hit.</returns>
    public bool TraceRay(Vector3 from, Vector3 to, string collisionName, ref Rubikon.TraceResult result)
    {
        var hitEntity = false;

        foreach (var entity in entities)
        {
            if (!entity.IsCollidable)
            {
                continue;
            }

            var entityTrace = entity.Collider!.TraceRay(from, to, collisionName);
            entityTrace.HitEntity = entity;

            hitEntity |= result.MinimizeWith(entityTrace);
        }

        return hitEntity;
    }

    /// <summary>Finds where a ray enters water that it is still under at its end.</summary>
    /// <param name="from">Where the ray starts, above the water.</param>
    /// <param name="to">Where the ray ends.</param>
    /// <param name="surface">The point on the water surface the ray goes in at.</param>
    /// <returns><see langword="true"/> when the ray ends under water.</returns>
    public bool TraceWaterSurface(Vector3 from, Vector3 to, out Vector3 surface)
    {
        surface = default;

        var nearest = new Rubikon.TraceResult();

        foreach (var entity in entities)
        {
            if (entity is FuncWater { IsRemoved: false, IsInQueryWorld: true, Collider: { IsEmpty: false } collider })
            {
                nearest.MinimizeWith(TraceWater(collider, from, to));
            }
        }

        var direction = to - from;

        // The first surface met has to face the ray, or the ray started under water
        if (!nearest.Hit || Vector3.Dot(nearest.HitNormal, direction) >= 0f)
        {
            return false;
        }

        // Tested a little short of the end, which sits right on whatever stopped the ray
        direction = Vector3.Normalize(direction);
        var depth = Vector3.Distance(nearest.HitPosition, to);
        var end = to - direction * MathF.Min(WaterSurfaceSkin, depth * 0.5f);
        var inside = nearest.HitPosition + direction * MathF.Min(WaterSurfaceSkin, depth * 0.25f);

        foreach (var entity in entities)
        {
            if (entity is FuncWater { IsRemoved: false, IsInQueryWorld: true, Collider: { IsEmpty: false } collider } && IsUnderWater(collider))
            {
                surface = nearest.HitPosition;
                return true;
            }
        }

        return false;

        // A volume made of meshes has no inside to ask about, so there the ray must not come back out of it
        bool IsUnderWater(EntityCollider collider)
            => collider.ContainsPoint(end)
                || (collider.Shape.Meshes.Length > 0 && TraceWater(collider, from, inside).Hit && !TraceWater(collider, inside, end).Hit);

        // Not every water volume is tagged as water; some are plain untagged geometry
        static Rubikon.TraceResult TraceWater(EntityCollider collider, Vector3 from, Vector3 to)
        {
            var trace = collider.TraceRay(from, to, Rubikon.WaterCollisionName);
            trace.MinimizeWith(collider.TraceRay(from, to, Rubikon.DefaultGeometry));

            return trace;
        }
    }

    private const float WaterSurfaceSkin = 0.25f;

    /// <summary>Finds the nearest usable entity along a ray, for the player's <c>+use</c>.</summary>
    /// <returns>The nearest usable entity in reach, or <see langword="null"/> when there is none.</returns>
    public BaseEntity? FindUseTarget(Vector3 from, Vector3 to)
    {
        // Seeded with the world, so a wall between the player and a button wins the trace
        var nearest = PhysicsWorld.TraceRay(from, to, Rubikon.Cs2PlayerCollisionFilter);
        BaseEntity? target = null;

        foreach (var entity in entities)
        {
            if (entity.IsRemoved
                || !entity.IsInQueryWorld
                || (entity.ObjectCaps & EntityCapability.UsableMask) == 0
                || entity.Collider is not { IsEmpty: false } collider)
            {
                continue;
            }

            if (nearest.MinimizeWith(collider.TraceRay(from, to, Rubikon.Cs2PlayerCollisionFilter)))
            {
                target = entity;
            }
        }

        return target;
    }

    /// <summary>Fires an entity I/O input at one entity after a delay in seconds.</summary>
    public void QueueInput(BaseEntity target, string inputName, string? parameter = null,
        BaseEntity? activator = null, BaseEntity? caller = null, float delay = 0f)
    {
        var fireTime = CurrentTime + MathF.Max(delay, 0f);

        Enqueue(new QueuedInput(target, null, inputName, parameter, activator, caller, fireTime, sequence++, null));
    }

    /// <summary>
    /// Fires an entity I/O input at every entity whose targetname matches, or failing that classname,
    /// <c>*</c> and <c>?</c> wildcards included, after a delay in seconds.
    /// </summary>
    public void QueueInputByTargetName(string targetName, string inputName, string? parameter = null,
        BaseEntity? activator = null, BaseEntity? caller = null, float delay = 0f)
    {
        var target = new EntityIOTarget(targetName, EntityIOTargetType.EntityNameOrClassName);
        var fireTime = CurrentTime + MathF.Max(delay, 0f);

        Enqueue(new QueuedInput(null, target, inputName, parameter, activator, caller, fireTime, sequence++, null));
    }

    // The connection is passed when it limits how often it may fire
    private void QueueInputByTarget(EntityIOTarget target, string inputName, string? parameter,
        BaseEntity? activator, BaseEntity? caller, float delay, EntityLump.Connection? connection)
    {
        var fireTime = CurrentTime + MathF.Max(delay, 0f);

        Enqueue(new QueuedInput(null, target, inputName, parameter, activator, caller, fireTime, sequence++, connection));
    }

    /// <summary>
    /// Drops the unfired inputs an entity queued itself. An input another entity aimed at it is that
    /// entity's to cancel.
    /// </summary>
    public void CancelQueuedInputsFrom(BaseEntity caller)
        => inputQueue.RemoveAll(input => input.Caller == caller);

    private void Enqueue(QueuedInput input)
    {
        // Kept in fire order, ties by queue order. Inserting into an almost-sorted list beats sorting at
        // dispatch, and keeps connections with different delays from inverting inside one tick.
        var index = inputQueue.Count;

        while (index > 0)
        {
            var previous = inputQueue[index - 1];

            if (previous.FireTime < input.FireTime
                || (previous.FireTime == input.FireTime && previous.Sequence < input.Sequence))
            {
                break;
            }

            index--;
        }

        inputQueue.Insert(index, input);
    }

    /// <summary>
    /// Fires an entity's authored output on every connection with that name. The value is what the output
    /// reports, and a connection's own parameter overrides it. The caller defaults to
    /// <paramref name="source"/>; the few outputs that pass on the caller of the input that fired them name it.
    /// </summary>
    public void TriggerOutput(BaseEntity source, string outputName, BaseEntity? activator = null, string? value = null,
        BaseEntity? caller = null)
    {
        if (source.Data?.Connections == null)
        {
            return;
        }

        foreach (var connection in source.Data.Connections)
        {
            if (!connection.OutputName.Equals(outputName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Hammer's "fire once only" and other limited counts
            if (connection.TimesToFire >= 0 && FiredCount(connection) >= connection.TimesToFire)
            {
                continue;
            }

            QueueInputByTarget(ConnectionTarget(connection, source.NameFixup),
                connection.InputName, ConnectionParameter(connection, source.NameFixup, value), activator, caller ?? source, connection.Delay, connection);
        }
    }

    /// <summary>Fires one authored connection on its own, for triggering map logic by hand.</summary>
    /// <param name="connection">The connection to fire.</param>
    /// <param name="activator">The entity that started the chain, usually the player.</param>
    public void QueueConnection(EntityLump.Connection connection, BaseEntity? activator = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var source = entities.Find(entity => !entity.IsRemoved && entity.Data == connection.SourceEntity);
        var nameFixup = source?.NameFixup ?? EntityNameFixup.None;

        QueueInputByTarget(ConnectionTarget(connection, nameFixup),
            connection.InputName, ConnectionParameter(connection, nameFixup, null), activator, source, 0f, null);
    }

    // The connection's target name with the source entity's name fixup applied
    internal static EntityIOTarget ConnectionTarget(EntityLump.Connection connection, EntityNameFixup nameFixup)
        => new(nameFixup.Apply(connection.TargetName), connection.TargetType);

    // A connection's own parameter replaces the output's value, and gets the name fixup too
    private static string? ConnectionParameter(EntityLump.Connection connection, EntityNameFixup nameFixup, string? value)
        => string.IsNullOrEmpty(connection.OverrideParam) || connection.OverrideParam == "(null)"
            ? value
            : nameFixup.Apply(connection.OverrideParam);

    /// <summary>Finds every entity whose targetname matches, in any world group.</summary>
    public IEnumerable<BaseEntity> FindAllByTargetName(string pattern)
    {
        foreach (var entity in NameCandidates(pattern))
        {
            if (Matches(entity, pattern))
            {
                yield return entity;
            }
        }
    }

    /// <summary>Finds the first entity whose targetname matches, in any world group.</summary>
    public BaseEntity? FindByTargetName(string pattern)
    {
        foreach (var entity in FindAllByTargetName(pattern))
        {
            return entity;
        }

        return null;
    }

    /// <summary>
    /// Finds every entity in the world group of <paramref name="scene"/> whose targetname matches. Used
    /// for <c>parentname</c> only; every other name is found in any world group.
    /// </summary>
    public IEnumerable<BaseEntity> FindAllByTargetNameInWorldGroup(string pattern, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        foreach (var entity in NameCandidates(pattern))
        {
            if (entity.Scene.WorldGroup == scene.WorldGroup && Matches(entity, pattern))
            {
                yield return entity;
            }
        }
    }

    private static bool Matches(BaseEntity entity, string pattern)
        => !entity.IsRemoved
        && entity.TargetName != null
        && EntityLump.EntityNameMatches(pattern, entity.TargetName);

    /// <summary>
    /// The entities a pattern can match: every entity for a wildcard pattern, otherwise only the ones with
    /// that name. Candidates still have to pass <see cref="Matches"/>.
    /// </summary>
    private List<BaseEntity> NameCandidates(string pattern)
    {
        if (pattern.AsSpan().IndexOfAny('*', '?') >= 0)
        {
            return entities;
        }

        return entitiesByName.TryGetValue(NameKey(pattern), out var named) ? named : NoEntities;
    }

    /// <summary>Uppercases each character on its own, the way names compare when they hold no wildcards.</summary>
    private static string NameKey(string name)
        => string.Create(name.Length, name, static (key, name) =>
        {
            for (var i = 0; i < key.Length; i++)
            {
                key[i] = char.ToUpperInvariant(name[i]);
            }
        });

    // Delivers everything the clock has reached, and everything those deliveries queue for now.
    // Restarting from the head of the queue after each event lets a chain of zero-delay connections finish
    // in the tick that started it; one snapshot would spread an N-hop relay chain over N ticks.
    private void DispatchDueInputs()
    {
        // See MaxInputsPerTick
        var budget = MaxInputsPerTick;

        while (inputQueue.Count > 0 && inputQueue[0].FireTime <= CurrentTime)
        {
            var input = inputQueue[0];

            inputQueue.RemoveAt(0);

            if (--budget < 0)
            {
                Logger.LogWarning("Entity I/O ran away: more than {Limit} inputs in one tick, dropping the rest", MaxInputsPerTick);
                inputQueue.Clear();
                return;
            }

            Deliver(input);
        }
    }

    // The target is resolved here, not when queued, so a delayed input reaches whatever holds the name at
    // delivery, including an entity that spawned during the delay.
    private void Deliver(QueuedInput input)
    {
        var data = new EntityInputData
        {
            Parameter = input.Parameter,
            Activator = input.Activator,
            Caller = input.Caller,
        };

        if (input.Connection is { } connection)
        {
            firedCounts[connection] = FiredCount(connection) + 1;
        }

        if (input.Target is { } bound)
        {
            if (!bound.IsRemoved)
            {
                bound.AcceptInput(input.InputName, data);
            }

            return;
        }

        if (input.NamedTarget is not { } target)
        {
            return;
        }

        // Copied to a list of its own, not a shared buffer: a handler may spawn or remove entities or
        // deliver further inputs, which would disturb a walk over state the nested delivery also uses
        var targets = new List<BaseEntity>();

        targets.AddRange(FindTargets(target, input.Activator, input.Caller));

        foreach (var entity in targets)
        {
            if (!entity.IsRemoved)
            {
                entity.AcceptInput(input.InputName, data);
            }
        }
    }

    private static bool IsProceduralName(string targetName, string proceduralName)
        => targetName.Equals(proceduralName, StringComparison.OrdinalIgnoreCase);

    private int FiredCount(EntityLump.Connection connection)
        => firedCounts.TryGetValue(connection, out var count) ? count : 0;

    /// <summary>
    /// Resolves what an authored connection addresses: <c>!</c> names for an entity in the firing chain,
    /// then names and classnames.
    /// </summary>
    /// <returns>The entities the target stands for, which may be none.</returns>
    public IEnumerable<BaseEntity> FindTargets(EntityIOTarget target, BaseEntity? activator = null, BaseEntity? caller = null)
    {
        // Both the target type and the name can name the chain; the name is the older spelling
        if (target.Type is EntityIOTargetType.SpecialActivator or EntityIOTargetType.SpecialCaller
            || (target.Name.Length > 0 && target.Name[0] == '!'))
        {
            // Matched case-insensitively
            var resolved = target.Type switch
            {
                EntityIOTargetType.SpecialActivator => activator,
                EntityIOTargetType.SpecialCaller => caller,
                _ when IsProceduralName(target.Name, "!activator") => activator,
                _ when IsProceduralName(target.Name, "!caller") => caller,
                _ when IsProceduralName(target.Name, "!self") => caller,
                _ when IsProceduralName(target.Name, "!player") => Player,
                _ => null,
            };

            if (resolved != null)
            {
                yield return resolved;
            }

            yield break;
        }

        if (string.IsNullOrEmpty(target.Name))
        {
            yield break;
        }

        // The other target types need more than map data (class inheritance, components, handles) and
        // match nothing rather than guess
        var byName = target.Type is EntityIOTargetType.EntityName or EntityIOTargetType.EntityNameOrClassName;
        var byClass = target.Type is EntityIOTargetType.ClassName or EntityIOTargetType.EntityNameOrClassName;

        if (!byName && !byClass)
        {
            yield break;
        }

        var matchedName = false;

        if (byName)
        {
            foreach (var entity in NameCandidates(target.Name))
            {
                if (Matches(entity, target.Name))
                {
                    matchedName = true;
                    yield return entity;
                }
            }
        }

        // The combined type falls back to the classname when no name matches
        if (!byClass || matchedName)
        {
            yield break;
        }

        foreach (var entity in entities)
        {
            if (!entity.IsRemoved && EntityLump.EntityNameMatches(target.Name, entity.Classname))
            {
                yield return entity;
            }
        }
    }

    private readonly record struct QueuedInput(
        BaseEntity? Target,
        EntityIOTarget? NamedTarget,
        string InputName,
        string? Parameter,
        BaseEntity? Activator,
        BaseEntity? Caller,
        float FireTime,
        long Sequence,
        EntityLump.Connection? Connection);
}
