using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
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
/// The entity context for a <see cref="Scene"/>: the world every simulated entity lives in. It owns the
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

    /// <summary>Gets the scene the entities render into.</summary>
    public Scene Scene { get; }

    /// <summary>Gets the loader entities use to pull their models and physics.</summary>
    public IFileLoader FileLoader => Scene.RendererContext.FileLoader;

    /// <summary>Gets the logger for entity problems.</summary>
    public ILogger Logger => Scene.RendererContext.Logger;

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
    private readonly List<QueuedInput> inputQueue = [];
    private readonly Dictionary<EntityLump.Connection, int> firedCounts = [];
    private long sequence;
    private float tickAccumulator;
    private bool hasRemovedEntities;

    /// <summary>
    /// Initializes an entity system for a scene. Prefer <see cref="Scene.EntitySystem"/> over constructing
    /// one directly; a scene has exactly one world.
    /// </summary>
    public EntitySystem(Scene scene)
    {
        Scene = scene;
    }

    /// <summary>
    /// Creates the entity for a map entity's keyvalues and puts it in the world. Returns
    /// <see langword="null"/> when the classname is not one the entity system implements, in which case
    /// the caller keeps ownership of it.
    /// </summary>
    /// <returns>The spawned entity, or <see langword="null"/> if the classname is not implemented.</returns>
    public BaseEntity? CreateEntity(Entity data, Matrix4x4 parentTransform, string? layerName)
    {
        var entity = EntityFactory.Create(this, new EntitySpawnInfo(data, parentTransform, layerName));

        if (entity == null)
        {
            return null;
        }

        Add(entity);

        return entity;
    }

    /// <summary>
    /// Puts the player into the world, so triggers have something to touch. Replaces any player already
    /// spawned.
    /// </summary>
    /// <returns>The player entity.</returns>
    public PlayerEntity SpawnPlayer(IPlayerController controller)
    {
        if (Player != null)
        {
            Remove(Player);
        }

        // Registration is what usually binds a class's inputs; the player never goes through the factory
        EntityInputTable.Bind<PlayerEntity>();

        Player = new PlayerEntity(this, controller);
        Player.Spawn();
        Add(Player);

        return Player;
    }

    private void Add(BaseEntity entity)
    {
        entities.Add(entity);
    }

    /// <summary>
    /// Runs <see cref="BaseEntity.Activate"/> on every entity, once the whole map has spawned.
    /// </summary>
    public void Activate()
    {
        for (var i = 0; i < entities.Count; i++)
        {
            entities[i].Activate();
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

            // Against the volume rather than its surface: a player standing well inside a big trigger is
            // still touching it. Rejects on world bounds first, so a trigger nowhere near costs one box test.
            var isOverlapping = volume.OverlapsVolume(center, halfExtents);

            entity.UpdateTouchLink(player, isOverlapping);
            player.UpdateTouchLink(entity, isOverlapping);
        }
    }

    /// <summary>
    /// Drops every entity and resets the clock. The scene nodes themselves are the scene's to clean up;
    /// this is what <see cref="Scene.Clear"/> calls once it has done that.
    /// </summary>
    public void Clear()
    {
        entities.Clear();
        Player = null;
        inputQueue.Clear();
        firedCounts.Clear();
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
        if (entities.Count == 0)
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

        // Entities are not scene nodes, so nothing else would place what they own
        foreach (var entity in entities)
        {
            entity.Update();
        }
    }

    private void Tick()
    {
        TickCount++;
        CurrentTime = TickCount * TickInterval;

        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];

            if (!entity.IsRemoved)
            {
                entity.Simulate(TickInterval);
            }
        }

        if (hasRemovedEntities)
        {
            entities.RemoveAll(static entity => entity.IsRemoved);
            hasRemovedEntities = false;
        }

        UpdateTouchLinks();

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

        foreach (var entity in entities)
        {
            if (!entity.IsCollidable || !entity.Collider!.MightHit(from, to, halfExtents))
            {
                continue;
            }

            hitEntity |= result.MinimizeWith(entity.Collider.TraceAABB(from, to, halfExtents, detectStartSolid));
        }

        return hitEntity;
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
    /// a connection authored with its own parameter overrides it, as in the engine.
    /// </summary>
    public void TriggerOutput(BaseEntity source, string outputName, BaseEntity? activator = null, string? value = null)
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

            // The authored override wins over whatever the output reports, which is the precedence
            // CBaseEntityOutput::FireOutput uses: a parameter on the connection replaces the value
            var parameter = string.IsNullOrEmpty(connection.OverrideParam) || connection.OverrideParam == "(null)"
                ? value
                : connection.OverrideParam;

            QueueInputByTarget(new EntityIOTarget(connection.TargetName, connection.TargetType),
                connection.InputName, parameter, activator, source, connection.Delay, connection);
        }
    }

    /// <summary>
    /// Finds every entity whose targetname matches.
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

        // A name that matches nothing falls back to the classname, as the loader's resolver does: the
        // combined type is the map saying "whichever of the two this turns out to be"
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
