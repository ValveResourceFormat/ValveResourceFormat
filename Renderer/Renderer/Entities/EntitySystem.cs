using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using Entity = ValveResourceFormat.ResourceTypes.EntityLump.Entity;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// What an authored entity I/O connection addresses: a name, and how the map means it to be read.
/// </summary>
/// <param name="Name">
/// The target name. May hold <c>*</c> and <c>?</c> wildcards, or be one of the <c>!</c> names that stand
/// for an entity in the firing chain rather than for anything the map named.
/// </param>
/// <param name="Type">Whether the name is a targetname or a classname.</param>
public readonly record struct EntityIOTarget(string Name, EntityIOTargetType Type);

/// <summary>
/// The world every simulated entity lives in. It owns the
/// list of living entities, runs them on a fixed tick, and carries the entity I/O queue between them.
/// </summary>
/// <remarks>
/// Entities tick at <see cref="TickInterval"/> rather than once per rendered frame. A frame runs as many
/// ticks as it owes, up to <see cref="MaxTicksPerFrame"/>, so a hitch cannot leave the world simulating
/// for longer than it takes to draw.
/// </remarks>
public sealed class EntitySystem
{
    /// <summary>The fixed simulation tick, matching the engine's default 64 tick.</summary>
    public const float TickInterval = 1f / 64f;

    /// <summary>
    /// How many inputs one tick may deliver before the world is assumed to be looping.
    /// </summary>
    /// <remarks>
    /// Not the engine's: <c>CEventQueue::ServiceEvents</c> has no limit, so a zero-delay cycle hangs the
    /// server, which is accepted there but not in a viewer opening someone else's map. The limit is far
    /// above real wiring - a large jailbreak map has under two thousand connections in total and a tick
    /// delivers a few dozen - so hitting it means a cycle, not a busy moment.
    /// </remarks>
    private const int MaxInputsPerTick = 100_000;

    /// <summary>The most ticks one frame may run before the leftover time is dropped.</summary>
    public const int MaxTicksPerFrame = 8;

    /// <summary>Gets the shared renderer context this world loads and logs through.</summary>
    public RendererContext RendererContext { get; }

    /// <summary>
    /// Gets the physics world of the main world group, the one every trace is made against: the player's
    /// movement, use, pushers and the rest. It holds the world physics of every spawn group loaded into
    /// that group.
    /// </summary>
    public PhysicsWorld PhysicsWorld { get; } = new();

    // Every other world group has a physics world of its own, such as a 3D sky's, which nothing traces
    private readonly Dictionary<string, PhysicsWorld> worldGroupPhysicsWorlds = [];

    /// <summary>
    /// Gets the physics world of a world group, creating it the first time it is asked for.
    /// </summary>
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

    /// <summary>Gets the loader entities use to pull their models and physics.</summary>
    public IFileLoader FileLoader => RendererContext.FileLoader;

    /// <summary>Gets the logger for entity problems.</summary>
    public ILogger Logger => RendererContext.Logger;

    /// <summary>Gets every living entity, in spawn order.</summary>
    public IReadOnlyList<BaseEntity> Entities => entities;

    /// <summary>
    /// Gets or sets whether the world is simulated. Switching it off holds every entity where it stands:
    /// the ticks stop, so nothing thinks, moves, touches, or fires entity I/O. What is already spawned
    /// stays in the scene and stays drawn.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets the player, once one has been spawned into this world.</summary>
    public PlayerEntity? Player { get; private set; }

    /// <summary>
    /// Gets the world entity at the root of the entity hierarchy, once a map has loaded. A trace that
    /// hits the static world reports it as the entity it hit.
    /// </summary>
    public WorldEntity? World { get; private set; }

    /// <summary>Gets the current simulation time in seconds, the engine's <c>curtime</c>.</summary>
    public float CurrentTime { get; private set; }

    /// <summary>
    /// Rounds a time onto the tick it lands nearest, the way the engine's <c>TIME_TO_TICKS</c> does.
    /// </summary>
    /// <remarks>
    /// Scheduling to the first tick at or after the time would round every interval up: a repeating 0.1s
    /// think would run every 7 ticks instead of 6, about a sixth slow.
    /// </remarks>
    /// <returns>The time of the nearest tick.</returns>
    public static float SnapToTick(float time) => (int)(0.5f + (time / TickInterval)) * TickInterval;

    /// <summary>Gets the number of ticks simulated so far.</summary>
    public int TickCount { get; private set; }

    /// <summary>
    /// Gets how far the current frame sits between the last two simulated ticks, in [0, 1). Entities
    /// interpolate their render transform across that span so movement is smooth at any framerate rather
    /// than stepping at the tick rate.
    /// </summary>
    public float InterpolationFraction => tickAccumulator / TickInterval;

    // Walked by index everywhere: a think, an input, or a touch handler may spawn or remove entities
    // part way through, and one spawned mid-walk is meant to be reached by it
    private readonly List<BaseEntity> entities = [];
    private readonly List<BaseEntity> parented = [];

    private readonly List<QueuedInput> inputQueue = [];
    private readonly Dictionary<EntityLump.Connection, int> firedCounts = [];
    private readonly HashSet<BaseEntity> playerImpacts = [];
    private long sequence;
    private float tickAccumulator;
    private bool hasRemovedEntities;

    /// <summary>
    /// Initializes an entity world. Prefer <see cref="Renderer.EntitySystem"/> over constructing one
    /// directly.
    /// </summary>
    /// <param name="context">The shared renderer context entities load and log through.</param>
    public EntitySystem(RendererContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        RendererContext = context;
    }

    /// <summary>
    /// Creates the entity for a map entity's keyvalues and puts it in the world. A classname the entity
    /// system does not implement becomes a <see cref="GenericModelEntity"/> or a <see cref="GenericEntity"/>,
    /// which draw themselves but do nothing else.
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

        // The engine binds the move parent before spawning, so a child's Spawn sees its pose local to the
        // parent. The parent may not exist yet, so the child spawns in Activate once it is bound.
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

    /// <summary>
    /// Puts the player into the world, so triggers have something to touch. Replaces any player already
    /// spawned.
    /// </summary>
    /// <returns>The player entity.</returns>
    public PlayerEntity SpawnPlayer(IPlayerController controller, Scene scene)
    {
        if (Player != null)
        {
            Remove(Player);
        }

        // Registration is what usually binds a class's inputs; the player never goes through the factory
        EntityInputTable.Bind<PlayerEntity>();

        Player = new PlayerEntity(this, scene, controller);
        Player.Spawn();
        Add(Player);

        return Player;
    }

    private void Add(BaseEntity entity)
    {
        entity.Owner ??= World;

        entities.Add(entity);
    }

    /// <summary>
    /// Creates the world entity when the first map loads, before any of its entities, so it owns all of
    /// them. It stays until <see cref="Clear"/>, whatever spawn groups come and go.
    /// </summary>
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

    /// <summary>Puts an entity built in code, rather than from map keyvalues, into the world.</summary>
    public void AddEntity(BaseEntity entity) => Add(entity);

    // Numbers the placed maps that set their names apart, never reused while the game runs
    private int nameFixupCount;

    /// <summary>Gets the number for the next placed map that sets its names apart with a prefix of its own.</summary>
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

        // Only the group's own collision; the world group keeps its physics world, as the engine's does
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
    /// Runs <see cref="BaseEntity.Activate"/> on every entity spawned since the last call. Called once
    /// per spawn group - the map, and again for its 3D skybox - the way the engine activates each group
    /// as it finishes spawning.
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

    /// <summary>
    /// Resolves an entity's move parent and lists it in <see cref="parented"/> behind its whole parent
    /// chain, the order children tick and draw in.
    /// </summary>
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

    /// <summary>
    /// Removes an entity from the world and takes its nodes out of the scene.
    /// </summary>
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

    /// <summary>
    /// Tests every trigger volume against the player, opening and closing touch links as they change. Both
    /// sides of a touch hear about it, the way the engine marks a pair of entities as touching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the player is tested. Testing every pair would be general, but costs a test per trigger per
    /// entity per tick in a map full of entities that never move, and the player is the only thing here
    /// that walks into a volume. So an entity the simulation moves into a trigger does not fire it. The
    /// touch machinery stays general, so this is the one place to widen if that changes.
    /// </para>
    /// <para>
    /// Run on the tick only, because touch handlers are entity logic: they teleport things, queue inputs
    /// against <see cref="CurrentTime"/>, and spawn or remove entities, all of which would become
    /// framerate-dependent if sampled per frame. The player still moves per frame, so a touch resolves up
    /// to one tick late, reading the player's live position.
    /// </para>
    /// </remarks>
    private void UpdateTouchLinks()
    {
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];

            if (!entity.IsTrigger || entity.IsRemoved || entity.Collider is not { IsEmpty: false } volume)
            {
                continue;
            }

            // Re-read every time rather than once: a trigger earlier in this pass may have teleported the
            // player, and the rest of the pass has to test where they are now, not where they set off from
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

            // Against the volume rather than its surface: a player standing well inside a big trigger is
            // still touching it. Rejects on world bounds first, so a trigger nowhere near costs one box test.
            var isOverlapping = volume.OverlapsVolume(center, halfExtents);

            entity.UpdateTouchLink(player, isOverlapping);
            player.UpdateTouchLink(entity, isOverlapping);
        }
    }

    /// <summary>
    /// Records that the player's movement ran into a solid entity, for the next tick to report as a touch.
    /// </summary>
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
    /// Drops every entity and resets the clock. The scene nodes themselves are the scene's to clean up,
    /// which <see cref="Renderer.Clear"/> does before calling this.
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
        hasRemovedEntities = false;
        tickAccumulator = 0f;
        CurrentTime = 0f;
        TickCount = 0;
    }

    /// <summary>
    /// Advances the world by a rendered frame's worth of time, running whole ticks.
    /// </summary>
    public void Update(float frameTime)
    {
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
            // Time spent paused is not owed back: switching it on again resumes rather than catching up,
            // and the entities rest on their last tick state rather than part way to the next one
            tickAccumulator = 0f;
        }

        // Entities are not scene nodes, so nothing else would place what they own. Parents first, as a
        // child is drawn in the frame its parent was just drawn at.
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
            hasRemovedEntities = false;
        }

        UpdateTouchLinks();
        DispatchPlayerImpacts();

        // Last, as the engine services its event queue after everything has moved and touched. A touch
        // handler's outputs therefore land in the tick that saw the touch rather than the one after it,
        // and a think scheduled for the current time waits for the next tick, as it does there.
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

            // A hull inside this entity cannot be swept - the SAT sweep is meaningless from an
            // overlapping start. A move whose endpoint is fully outside steps out freely, anything
            // else stops where it stands: escape is always possible, crossing the interior never is,
            // and a pusher never leaves the player deep inside it.
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
    /// Finds the nearest usable entity along a ray, for the player's <c>+use</c>.
    /// </summary>
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

    /// <summary>
    /// Fires an entity I/O input at one entity, after a delay in seconds.
    /// </summary>
    public void QueueInput(BaseEntity target, string inputName, string? parameter = null,
        BaseEntity? activator = null, BaseEntity? caller = null, float delay = 0f)
    {
        var fireTime = CurrentTime + MathF.Max(delay, 0f);

        Enqueue(new QueuedInput(target, null, inputName, parameter, activator, caller, fireTime, sequence++, null));
    }

    /// <summary>
    /// Fires an entity I/O input at every entity whose targetname matches, <c>*</c> and <c>?</c> wildcards
    /// included, after a delay in seconds.
    /// </summary>
    public void QueueInputByTargetName(string targetName, string inputName, string? parameter = null,
        BaseEntity? activator = null, BaseEntity? caller = null, float delay = 0f)
    {
        var target = new EntityIOTarget(targetName, EntityIOTargetType.EntityNameOrClassName);
        var fireTime = CurrentTime + MathF.Max(delay, 0f);

        Enqueue(new QueuedInput(null, target, inputName, parameter, activator, caller, fireTime, sequence++, null));
    }

    /// <summary>
    /// Queues an input by target, the way an authored connection addresses one. The connection is passed
    /// when one limits how often it may fire.
    /// </summary>
    private void QueueInputByTarget(EntityIOTarget target, string inputName, string? parameter,
        BaseEntity? activator, BaseEntity? caller, float delay, EntityLump.Connection? connection)
    {
        var fireTime = CurrentTime + MathF.Max(delay, 0f);

        Enqueue(new QueuedInput(null, target, inputName, parameter, activator, caller, fireTime, sequence++, connection));
    }

    /// <summary>
    /// Drops the inputs an entity queued that have not fired yet, Source's <c>CancelPending</c>. Only the
    /// ones it queued itself: an input another entity aimed at it is that entity's to cancel.
    /// </summary>
    public void CancelQueuedInputsFrom(BaseEntity caller)
        => inputQueue.RemoveAll(input => input.Caller == caller);

    private void Enqueue(QueuedInput input)
    {
        // Kept in fire order, ties broken by the order they were queued, which is what the engine's
        // time-ordered queue amounts to. Inserting into an almost-sorted list beats sorting at dispatch,
        // and it keeps two connections with different delays from inverting inside one tick.
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
    /// Fires one of an entity's authored outputs, delivering it to every connection with that name.
    /// Source's <c>FireOutput</c>. The value is what the output reports, for the ones that carry a reading;
    /// a connection authored with its own parameter overrides it, as in the engine. The caller defaults to
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

            // Hammer's "fire once only", which the map counts on for anything that must not repeat
            if (connection.TimesToFire >= 0 && FiredCount(connection) >= connection.TimesToFire)
            {
                continue;
            }

            QueueInputByTarget(ConnectionTarget(connection, source.NameFixup),
                connection.InputName, ConnectionParameter(connection, source.NameFixup, value), activator, caller ?? source, connection.Delay, connection);
        }
    }

    /// <summary>
    /// Fires one authored connection on its own, for triggering map logic by
    /// hand.
    /// </summary>
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

    /// <summary>
    /// The target of an authored connection, named as the source entity's spawn group names its entities.
    /// </summary>
    internal static EntityIOTarget ConnectionTarget(EntityLump.Connection connection, EntityNameFixup nameFixup)
        => new(nameFixup.Apply(connection.TargetName), connection.TargetType);

    /// <summary>
    /// The authored override wins over whatever the output reports, which is the precedence
    /// CBaseEntityOutput::FireOutput uses: a parameter on the connection replaces the value. The override
    /// gets the source entity's name fixup too.
    /// </summary>
    private static string? ConnectionParameter(EntityLump.Connection connection, EntityNameFixup nameFixup, string? value)
        => string.IsNullOrEmpty(connection.OverrideParam) || connection.OverrideParam == "(null)"
            ? value
            : nameFixup.Apply(connection.OverrideParam);

    /// <summary>
    /// Finds every entity whose targetname matches, in any world group, as the engine's name lookups do.
    /// </summary>
    public IEnumerable<BaseEntity> FindAllByTargetName(string pattern)
    {
        foreach (var entity in entities)
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
    /// Finds every entity in the world group of <paramref name="scene"/> whose targetname matches. Only
    /// <c>parentname</c> is looked up this way; every other name an entity refers to is found in any world group.
    /// </summary>
    public IEnumerable<BaseEntity> FindAllByTargetNameInWorldGroup(string pattern, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        foreach (var entity in entities)
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
    /// Delivers everything the clock has reached, and everything those deliveries queue for now.
    /// </summary>
    /// <remarks>
    /// The engine's <c>CEventQueue::ServiceEvents</c> restarts from the head of the queue after each event,
    /// so a chain of zero-delay connections finishes in the tick that started it. Taking one snapshot
    /// instead would spread an N-hop relay chain over N ticks, which a map expecting a button to act at
    /// once would notice. The only difference here is <see cref="MaxInputsPerTick"/>.
    /// </remarks>
    private void DispatchDueInputs()
    {
        // The engine has no guard here and will spin forever on a cycle; see MaxInputsPerTick
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

    /// <summary>
    /// Hands one queued input to whoever answers to its target now. The target is resolved here rather
    /// than when the input was queued, as the engine does: a delayed input goes to whatever holds the
    /// name at delivery, including an entity that spawned during the delay.
    /// </summary>
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

        // Copied out, and into a list of its own rather than a shared buffer: a handler may spawn or
        // remove entities, and may deliver further inputs, either of which would disturb a walk over
        // state the next delivery down also uses
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

    /// <summary>Whether a target name is the given <c>!</c> name, whatever case the map wrote it in.</summary>
    private static bool IsProceduralName(string targetName, string proceduralName)
        => targetName.Equals(proceduralName, StringComparison.OrdinalIgnoreCase);

    /// <summary>How many times a connection has fired, for the ones a map limited.</summary>
    private int FiredCount(EntityLump.Connection connection)
        => firedCounts.TryGetValue(connection, out var count) ? count : 0;

    /// <summary>
    /// Resolves what an authored connection addresses: the <c>!</c> names that stand for an entity in the
    /// firing chain, then names and classnames.
    /// </summary>
    /// <returns>The entities the target stands for, which may be none.</returns>
    public IEnumerable<BaseEntity> FindTargets(EntityIOTarget target, BaseEntity? activator = null, BaseEntity? caller = null)
    {
        // The target type can name the chain outright, and so can the name, which is how the older
        // spelling of the same idea reaches here. Source's FindEntityProcedural.
        if (target.Type is EntityIOTargetType.SpecialActivator or EntityIOTargetType.SpecialCaller
            || (target.Name.Length > 0 && target.Name[0] == '!'))
        {
            // Spelled the way a mapper writes them, and matched the way the engine matches them: its
            // FindEntityProcedural compares with FStrEq, which is case-insensitive
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

        // Anything else addresses entities by what they are called or what they are. The rest of the
        // type set asks questions map data alone cannot answer - class inheritance, components, handles -
        // and matches nothing rather than guessing.
        var byName = target.Type is EntityIOTargetType.EntityName or EntityIOTargetType.EntityNameOrClassName;
        var byClass = target.Type is EntityIOTargetType.ClassName or EntityIOTargetType.EntityNameOrClassName;

        if (!byName && !byClass)
        {
            yield break;
        }

        var matchedName = false;

        foreach (var entity in entities)
        {
            if (byName && !entity.IsRemoved && entity.TargetName != null
                && EntityLump.EntityNameMatches(target.Name, entity.TargetName))
            {
                matchedName = true;
                yield return entity;
            }
        }

        // A name that matches nothing falls back to the classname: the combined type is the map saying
        // "whichever of the two this turns out to be"
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
