using System.IO;
using Box3D;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;
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

    private Dictionary<string, CurveLightStyle>? lightStyles;

    /// <summary>Gets the light styles of the game by name.</summary>
    internal IReadOnlyDictionary<string, CurveLightStyle> LightStyles => lightStyles ??= CurveLightStyle.LoadAll(FileLoader);

    /// <summary>Gets the logger for entity problems.</summary>
    public ILogger Logger => RendererContext.Logger;

    /// <summary>Gets every living entity, in spawn order.</summary>
    public IReadOnlyList<BaseEntity> Entities => entities;

    internal List<SkyCameraVolume> SkyCameraVolumes { get; } = [];

    /// <summary>
    /// Gets or sets whether the world is simulated. When off the ticks stop, so nothing thinks, moves,
    /// touches or fires entity I/O. Spawned entities stay in the scene and stay drawn.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets the player, once one has been spawned into this world.</summary>
    public PlayerEntity? Player { get; private set; }

    /// <summary>
    /// Gets or sets what a tool draws for entities beyond what the game shows, or <see langword="null"/> to
    /// draw only that. Set before a world is loaded, as entities build their nodes when they spawn.
    /// </summary>
    public IEntityToolVisuals? ToolVisuals { get; set; }

    /// <summary>
    /// Gets the world entity at the root of the hierarchy, once a map has loaded. A trace that hits the
    /// static world reports it as the entity hit.
    /// </summary>
    public WorldEntity? World { get; private set; }

    /// <summary>
    /// Gets the rigid body world, creating it on first use. The world loader pushes the static
    /// geometry through here, physics props add their bodies, and <see cref="Update"/> steps it every frame.
    /// </summary>
    public PhysicsSimulation Physics => physics ??= new PhysicsSimulation(FileLoader);

    /// <summary>
    /// Gets the rigid body world if anything has created one, without creating it: the player's
    /// pickup logic has nothing to do in a scene with no simulated physics.
    /// </summary>
    public PhysicsSimulation? PhysicsOrNull => physics;

    /// <summary>Gets the game's breakable prop base classes, loaded on first use.</summary>
    public PropDataTable PropData => propData ??= PropDataTable.Load(FileLoader);

    private PropDataTable? propData;

    // Reused each frame to gather physics impacts before any of them breaks something
    private readonly Dictionary<PropPhysics, float> impactSpeeds = [];

    // Turns this step's hard contacts into impact damage for breakable props: a pot dropped from a
    // shelf, a bottle thrown at a wall. The player's pushing hull shoves props rather than striking
    // them, so its contacts are left out. The same hits play the impact sounds, in the physics step.
    private void DispatchPhysicsImpacts(PhysicsSimulation simulation)
    {
        impactSpeeds.Clear();

        foreach (var hit in simulation.World.Events.ContactHits)
        {
            if (!hit.ShapeA.IsValid || !hit.ShapeB.IsValid
                || ((hit.ShapeA.Filter.Categories | hit.ShapeB.Filter.Categories) & PhysicsSimulation.PlayerCategory) != 0)
            {
                continue;
            }

            NoteImpact(simulation, hit.ShapeA.Body, hit.ShapeB.Body, hit.ApproachSpeed);
            NoteImpact(simulation, hit.ShapeB.Body, hit.ShapeA.Body, hit.ApproachSpeed);
        }

        foreach (var (prop, speed) in impactSpeeds)
        {
            prop.TakeImpact(speed);
        }
    }

    // A light body striking a heavy one hurts it in proportion, so a tossed can does not shatter a
    // pot and falling debris does not chain-break everything it lands on; anything that does not
    // move under the hit - the world, a door - strikes with its full weight
    private void NoteImpact(PhysicsSimulation simulation, Body struck, Body other, float speed)
    {
        if (simulation.GetOwner(struck) is not PropPhysics { IsBreakable: true } prop)
        {
            return;
        }

        // What the player holds breaks nothing it bumps, unless the struck prop opts in: HL:A
        // opts in its crockery and nothing else
        if (!prop.AcceptsDamageFromHeldObjects && Player?.Carried is { } held && ReferenceEquals(simulation.GetOwner(other), held))
        {
            return;
        }

        if (other.Type == BodyType.Dynamic && struck.Mass > 0f)
        {
            speed *= MathF.Min(1f, MathF.Sqrt(other.Mass / struck.Mass));
        }

        // Several contact points of one collision report separately; the hardest one counts
        impactSpeeds[prop] = MathF.Max(impactSpeeds.GetValueOrDefault(prop), speed);
    }

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
    private bool warnedAboutRunawayInputs;
    private readonly Dictionary<EntityLump.Connection, int> firedCounts = [];
    private readonly HashSet<BaseEntity> playerImpacts = [];
    private long sequence;
    private float tickAccumulator;
    private bool hasRemovedEntities;
    private PhysicsSimulation? physics;

    /// <summary>
    /// Initializes an entity world. Prefer <see cref="Renderer.EntitySystem"/> over constructing one.
    /// </summary>
    /// <param name="context">The shared renderer context entities load and log through.</param>
    public EntitySystem(RendererContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        RendererContext = context;
        TempEntities = new TempEntities(this);
        SpawnGroupManager = new SpawnGroupManager(this);
    }

    /// <summary>Gets the one-shot effects that have no entity of their own, such as bullet impacts.</summary>
    public TempEntities TempEntities { get; }

    /// <summary>Gets what loads, spawns and unloads the spawn groups the entities come from.</summary>
    public SpawnGroupManager SpawnGroupManager { get; }

    /// <summary>
    /// Gets the active spawn group, which a <c>point_template</c> set to insert into it gives its entities to,
    /// set by <see cref="SpawnGroupManager.PerformActivateSpawnGroup"/>. The map becomes active once it has
    /// spawned, and none is active once the active one unloads.
    /// </summary>
    public SpawnGroup? ActiveSpawnGroup { get; internal set; }

    /// <summary>
    /// Gets the <c>skybox_reference</c> whose 3D sky is drawn, the last to activate, or <see langword="null"/>.
    /// </summary>
    public SkyboxReference? ActiveSkyboxReference { get; internal set; }

    /// <summary>
    /// Creates the entity for a map entity's keyvalues and puts it in the world, to be spawned with the rest of
    /// its spawn group by <see cref="SpawnEntities"/>. An unimplemented classname becomes a
    /// <see cref="GenericModelEntity"/> or <see cref="GenericEntity"/>, which only draw themselves.
    /// </summary>
    /// <param name="data">The entity's keyvalues.</param>
    /// <param name="layerName">Visibility layer for its nodes.</param>
    /// <param name="spawnGroup">The spawn group spawning it.</param>
    /// <returns>
    /// The created entity, or <see langword="null"/> if the keyvalues name no classname or a <c>worldspawn</c>.
    /// </returns>
    internal BaseEntity? CreateEntity(Entity data, string? layerName, SpawnGroup spawnGroup)
    {
        var entity = EntityFactory.Create(this, new EntitySpawnInfo(data, layerName, spawnGroup));

        if (entity != null)
        {
            Add(entity);
        }

        return entity;
    }

    /// <summary>
    /// Creates an entity at runtime and spawns and activates it there and then, into a spawn group that is
    /// already spawned, such as the pieces a prop breaks into. Its keyvalues place it in the space of the
    /// spawn group, as a map's own entities are.
    /// </summary>
    /// <param name="data">The entity's keyvalues.</param>
    /// <param name="layerName">Visibility layer for its nodes.</param>
    /// <param name="spawnGroup">The spawn group the entity joins.</param>
    /// <returns>The entity, or <see langword="null"/> if the keyvalues name no classname.</returns>
    public BaseEntity? SpawnEntity(Entity data, string? layerName, SpawnGroup spawnGroup)
    {
        var entity = CreateEntity(data, layerName, spawnGroup);

        if (entity != null)
        {
            SpawnEntities([entity]);
        }

        return entity;
    }

    /// <summary>
    /// Spawns and then activates a batch of entities created by <see cref="CreateEntity"/>, which a spawn group
    /// does once the groups it placed have finished.
    /// </summary>
    /// <param name="batch">The entities, in the order they were created.</param>
    internal void SpawnEntities(List<BaseEntity> batch)
    {
        // Move parents first, so every entity spawns and activates seeing the finished hierarchy
        foreach (var entity in batch)
        {
            ResolveMoveParentChain(entity);
        }

        // Shallowest in the hierarchy first, as in game, so a parent has spawned and placed itself before its
        // children spawn local to it. The game also orders each depth by class, which is not done here.
        var spawning = new HashSet<BaseEntity>(batch);

        foreach (var entity in batch)
        {
            if (entity.MoveParent == null && spawning.Remove(entity))
            {
                Spawn(entity);
            }
        }

        // By index, as a Spawn may spawn a group of its own
        for (var i = 0; i < parented.Count; i++)
        {
            if (spawning.Remove(parented[i]))
            {
                Spawn(parented[i]);
            }
        }

        foreach (var entity in batch)
        {
            if (!entity.IsRemoved)
            {
                entity.Activate();
            }
        }
    }

    private static void Spawn(BaseEntity entity)
    {
        if (entity.IsRemoved)
        {
            return;
        }

        try
        {
            entity.Spawn();
        }
        catch (Exception e)
        {
            throw EntityException(entity.Data, entity.Classname, e);
        }
    }

    internal static InvalidDataException EntityException(Entity? data, string classname, Exception inner)
    {
        var id = data?.GetStringProperty("hammeruniqueid", string.Empty);

        return new InvalidDataException($"Failed to process entity '{classname}' (hammeruniqueid={id})", inner);
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
    /// Drops every entity and the collision of the loaded worlds, and resets the clock. Scene nodes are
    /// cleaned up by <see cref="Renderer.Clear"/>, which runs first.
    /// </summary>
    public void Clear()
    {
        RemoveAllEntities();
        SpawnGroupManager.ForgetPlacedWorlds();

        PhysicsWorld.Clear();
        worldGroupPhysicsWorlds.Clear();
        physics?.Dispose();
        physics = null;
    }

    /// <summary>
    /// Removes every entity and resets the clock, keeping the collision of the loaded worlds, so their
    /// entities can spawn again as they did when the map loaded.
    /// </summary>
    public void RemoveAllEntities()
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
        World = null;
        Player = null;
        ActiveSpawnGroup = null;
        ActiveSkyboxReference = null;
        SpawnGroupManager.Clear();
        inputQueue.Clear();
        firedCounts.Clear();
        playerImpacts.Clear();
        TempEntities.Clear();
        hasRemovedEntities = false;
        tickAccumulator = 0f;
        CurrentTime = 0f;
        TickCount = 0;
    }

    /// <summary>
    /// Gets the camera the current frame is drawn with, or <see langword="null"/> outside a frame.
    /// The input camera and this can differ - view smoothing and view punch sit between them - so
    /// anything drawn relative to the view must read this one, not the controller's.
    /// </summary>
    public Camera? RenderCamera { get; private set; }

    /// <summary>Advances the world by a frame's worth of time, running whole ticks.</summary>
    /// <param name="frameTime">Elapsed time in seconds since the last frame.</param>
    /// <param name="renderCamera">The camera the frame is drawn with, for <see cref="RenderCamera"/>.</param>
    public void Update(float frameTime, Camera? renderCamera = null)
    {
        RenderCamera = renderCamera;
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

        if (Enabled)
        {
            // The rigid body world steps with the rendered frame, like the player movement does:
            // physics that only ever saw the camera at the tick rate reads back quantized however
            // it is drawn. Steering runs first and drawing happens after, so what the carry asks
            // for this frame is on screen this frame. The entity tick above stays the cadence of
            // game logic; this is only the integrator.
            var physicsStep = MathF.Min(frameTime, MaxPhysicsFrameStep);

            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];

                if (!entity.IsRemoved)
                {
                    entity.FrameSimulate(physicsStep);
                }
            }

            if (physics != null)
            {
                physics.Step(physicsStep);
                DispatchPhysicsImpacts(physics);
            }
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

    // Load stalls and breakpoints must not become one giant integration step
    private const float MaxPhysicsFrameStep = 0.1f;

    private void Tick()
    {
        TickCount++;
        CurrentTime = TickCount * TickInterval;

        SpawnGroupManager.SpawnPendingGroups();

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

    internal static bool Matches(BaseEntity entity, string pattern)
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

    /// <summary>Uppercases the ASCII letters of a name, the way names compare when they hold no wildcards.</summary>
    private static string NameKey(string name)
        => string.Create(name.Length, name, static (key, name) =>
        {
            for (var i = 0; i < key.Length; i++)
            {
                key[i] = EntityLump.ToUpperAscii(name[i]);
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
                if (!warnedAboutRunawayInputs)
                {
                    warnedAboutRunawayInputs = true;
                    Logger.LogWarning("Entity I/O ran away: more than {Limit} inputs in one tick, dropping the rest", MaxInputsPerTick);
                }

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
