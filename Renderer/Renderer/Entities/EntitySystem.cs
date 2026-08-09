using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using Entity = ValveResourceFormat.ResourceTypes.EntityLump.Entity;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The entity context for a <see cref="Scene"/>: the world every simulated entity lives in. It owns the
/// list of living entities, runs them on a fixed tick, and carries the entity I/O queue between them.
/// </summary>
/// <remarks>
/// Entities tick at <see cref="TickInterval"/> rather than once per rendered frame, because Source's entity
/// code is written against a fixed tick: think intervals, spin-up ramps and the like are expressed in whole
/// ticks and drift if you hand them a variable frame time. A frame runs as many ticks as it has time for,
/// up to <see cref="MaxTicksPerFrame"/>, so a hitch cannot make the world take longer to simulate than to
/// draw.
/// </remarks>
public sealed class EntitySystem
{
    /// <summary>The fixed simulation tick, matching the engine's default 64 tick.</summary>
    public const float TickInterval = 1f / 64f;

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

    /// <summary>Gets the current simulation time in seconds, the engine's <c>curtime</c>.</summary>
    public float CurrentTime { get; private set; }

    /// <summary>Gets the number of ticks simulated so far.</summary>
    public int TickCount { get; private set; }

    /// <summary>
    /// Gets how far the current frame sits between the last two simulated ticks, in [0, 1). Entities
    /// interpolate their render transform across that span so movement is smooth at any framerate rather
    /// than stepping at the tick rate.
    /// </summary>
    public float InterpolationFraction => tickAccumulator / TickInterval;

    private readonly List<BaseEntity> entities = [];
    private readonly List<QueuedInput> inputQueue = [];
    private readonly List<QueuedInput> dueInputs = [];
    private float tickAccumulator;
    private bool hasRemovedEntities;

    /// <summary>
    /// Initializes an entity system for a scene. Prefer <see cref="Scene.EntitySystem"/> over constructing
    /// one directly; a scene has exactly one world.
    /// </summary>
    /// <param name="scene">The scene the entities render into.</param>
    public EntitySystem(Scene scene)
    {
        Scene = scene;
    }

    /// <summary>
    /// Creates the entity for a map entity's keyvalues and puts it in the world. Returns
    /// <see langword="null"/> when the classname is not one the entity system implements, in which case
    /// the caller keeps ownership of it.
    /// </summary>
    /// <param name="data">The entity's keyvalues, as authored in the map.</param>
    /// <param name="parentTransform">Transform of the spawner, or identity for plain map entities.</param>
    /// <param name="layerName">Visibility layer for the entity and every node it creates.</param>
    /// <returns>The spawned entity, or <see langword="null"/> if the classname is not implemented.</returns>
    public BaseEntity? CreateEntity(Entity data, Matrix4x4 parentTransform, string? layerName)
    {
        var entity = EntityFactory.Create(this, new EntitySpawnInfo(data, parentTransform, layerName));

        if (entity == null)
        {
            return null;
        }

        entities.Add(entity);
        Scene.Add(entity, dynamic: true);

        return entity;
    }

    /// <summary>
    /// Runs <see cref="BaseEntity.Activate"/> on every entity, once the whole map has spawned.
    /// </summary>
    public void Activate()
    {
        foreach (var entity in entities)
        {
            entity.Activate();
        }
    }

    /// <summary>
    /// Removes an entity from the world and takes its nodes out of the scene.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    public void Remove(BaseEntity entity)
    {
        if (entity.IsRemoved)
        {
            return;
        }

        entity.RemoveFromScene();
        hasRemovedEntities = true;
    }

    /// <summary>
    /// Drops every entity and resets the clock. The scene nodes themselves are the scene's to clean up;
    /// this is what <see cref="Scene.Clear"/> calls once it has done that.
    /// </summary>
    public void Clear()
    {
        entities.Clear();
        inputQueue.Clear();
        dueInputs.Clear();
        hasRemovedEntities = false;
        tickAccumulator = 0f;
        CurrentTime = 0f;
        TickCount = 0;
    }

    /// <summary>
    /// Advances the world by a rendered frame's worth of time, running whole ticks.
    /// </summary>
    /// <param name="frameTime">Elapsed time in seconds since the last frame.</param>
    public void Update(float frameTime)
    {
        if (entities.Count == 0)
        {
            return;
        }

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

    private void Tick()
    {
        TickCount++;
        CurrentTime = TickCount * TickInterval;

        DispatchDueInputs();

        // Indexed, because an input or a think can spawn or remove entities mid-tick
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
    }

    /// <summary>
    /// Fires an entity I/O input at one entity, after its delay has elapsed.
    /// </summary>
    /// <param name="target">The entity receiving the input.</param>
    /// <param name="inputName">The input's name.</param>
    /// <param name="parameter">The parameter passed with the input, if any.</param>
    /// <param name="activator">The entity that started the I/O chain.</param>
    /// <param name="caller">The entity that fired the output.</param>
    /// <param name="delay">Seconds to wait before the input is delivered.</param>
    public void QueueInput(BaseEntity target, string inputName, string? parameter = null,
        BaseEntity? activator = null, BaseEntity? caller = null, float delay = 0f)
    {
        inputQueue.Add(new QueuedInput
        {
            Target = target,
            InputName = inputName,
            Parameter = parameter,
            Activator = activator,
            Caller = caller,
            FireTime = CurrentTime + MathF.Max(delay, 0f),
        });
    }

    /// <summary>
    /// Fires an entity I/O input at every entity whose targetname matches, wildcards included.
    /// </summary>
    /// <param name="targetName">Targetname to match, may contain <c>*</c> and <c>?</c>.</param>
    /// <param name="inputName">The input's name.</param>
    /// <param name="parameter">The parameter passed with the input, if any.</param>
    /// <param name="activator">The entity that started the I/O chain.</param>
    /// <param name="caller">The entity that fired the output.</param>
    /// <param name="delay">Seconds to wait before the input is delivered.</param>
    public void QueueInputByTargetName(string targetName, string inputName, string? parameter = null,
        BaseEntity? activator = null, BaseEntity? caller = null, float delay = 0f)
    {
        foreach (var target in FindAllByTargetName(targetName))
        {
            QueueInput(target, inputName, parameter, activator, caller, delay);
        }
    }

    /// <summary>
    /// Fires one of an entity's authored outputs, delivering it to every connection with that name.
    /// Source's <c>FireOutput</c>.
    /// </summary>
    /// <param name="source">The entity firing the output.</param>
    /// <param name="outputName">The output's name, as authored in the map.</param>
    /// <param name="activator">The entity that started the I/O chain.</param>
    public void TriggerOutput(BaseEntity source, string outputName, BaseEntity? activator = null)
    {
        if (source.Data.Connections == null)
        {
            return;
        }

        foreach (var connection in source.Data.Connections)
        {
            if (!connection.OutputName.Equals(outputName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parameter = string.IsNullOrEmpty(connection.OverrideParam) || connection.OverrideParam == "(null)"
                ? null
                : connection.OverrideParam;

            QueueInputByTargetName(connection.TargetName, connection.InputName, parameter, activator, source, connection.Delay);
        }
    }

    /// <summary>
    /// Finds the first entity whose targetname matches.
    /// </summary>
    /// <param name="pattern">Targetname to match, may contain <c>*</c> and <c>?</c>.</param>
    /// <returns>The entity, or <see langword="null"/> when nothing matches.</returns>
    public BaseEntity? FindByTargetName(string pattern)
    {
        foreach (var entity in entities)
        {
            if (Matches(entity, pattern))
            {
                return entity;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds every entity whose targetname matches.
    /// </summary>
    /// <param name="pattern">Targetname to match, may contain <c>*</c> and <c>?</c>.</param>
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

    private void DispatchDueInputs()
    {
        if (inputQueue.Count == 0)
        {
            return;
        }

        // Copied out first, in fire order, because handling an input can queue more of them
        var now = CurrentTime;

        foreach (var input in inputQueue)
        {
            if (input.FireTime <= now)
            {
                dueInputs.Add(input);
            }
        }

        inputQueue.RemoveAll(input => input.FireTime <= now);

        foreach (var input in dueInputs)
        {
            if (!input.Target.IsRemoved)
            {
                input.Target.AcceptInput(input.InputName, new EntityInputData
                {
                    Parameter = input.Parameter,
                    Activator = input.Activator,
                    Caller = input.Caller,
                });
            }
        }

        dueInputs.Clear();
    }

    private readonly struct QueuedInput
    {
        public required BaseEntity Target { get; init; }
        public required string InputName { get; init; }
        public required string? Parameter { get; init; }
        public required BaseEntity? Activator { get; init; }
        public required BaseEntity? Caller { get; init; }
        public required float FireTime { get; init; }
    }
}
