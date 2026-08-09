using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using Entity = ValveResourceFormat.ResourceTypes.EntityLump.Entity;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Everything <see cref="EntityFactory"/> needs to bring an entity into the world: its keyvalues, the
/// transform of whatever spawned it, and the visibility layer its scene nodes belong to.
/// </summary>
/// <param name="Data">The entity's keyvalues, as authored in the map.</param>
/// <param name="ParentTransform">Transform of the spawner (a template, or identity for map entities).</param>
/// <param name="LayerName">Visibility layer for this entity and every node it creates.</param>
public readonly record struct EntitySpawnInfo(Entity Data, Matrix4x4 ParentTransform, string? LayerName);

/// <summary>
/// A scene node an entity spawned and drives, and where it sits in the entity's own frame.
/// </summary>
/// <param name="Node">The scene node.</param>
/// <param name="LocalTransform">The node's transform relative to the entity.</param>
public readonly record struct EntityChild(SceneNode Node, Matrix4x4 LocalTransform);

/// <summary>
/// The base of the simulated entity hierarchy, Source's <c>CBaseEntity</c>. An entity <i>is</i> the scene
/// node for the thing it represents: it carries the origin and angles, ticks inside
/// <see cref="EntitySystem"/>, and owns the renderable nodes (model, physics hulls) it spawned as children.
/// </summary>
/// <remarks>
/// Movement is integrated on the entity system's fixed tick, not the render frame, so think intervals and
/// spin-up ramps land where the engine puts them regardless of framerate. Children are ordinary scene nodes
/// parented to this one, which keeps them out of <see cref="Scene"/>'s own update pass; this entity pushes
/// its transform down to them and refreshes their octree entries in <see cref="Update"/>.
/// </remarks>
public class BaseEntity : SceneNode
{
    /// <summary>Gets the world this entity lives in.</summary>
    public EntitySystem EntitySystem { get; }

    /// <summary>Gets the entity's keyvalues, as authored in the map.</summary>
    public Entity Data { get; }

    /// <summary>Gets the entity's <c>classname</c>.</summary>
    public string Classname { get; }

    /// <summary>Gets the entity's <c>targetname</c>, the name entity I/O addresses it by.</summary>
    public string? TargetName { get; }

    /// <summary>Gets the entity's <c>spawnflags</c>.</summary>
    public uint SpawnFlags { get; }

    /// <summary>Gets the transform of whatever spawned this entity; identity for plain map entities.</summary>
    public Matrix4x4 ParentTransform { get; }

    /// <summary>Gets the authored <c>scales</c>, which movement never changes.</summary>
    public Vector3 EntityScale { get; }

    /// <summary>Gets or sets the origin. Setting it rebuilds <see cref="SceneNode.Transform"/>.</summary>
    public Vector3 Origin
    {
        get;
        set
        {
            field = value;
            UpdateTransform();
        }
    }

    /// <summary>Gets or sets the orientation as a QAngle (pitch, yaw, roll) in degrees. Setting it rebuilds <see cref="SceneNode.Transform"/>.</summary>
    public Vector3 Angles
    {
        get;
        set
        {
            field = value;
            UpdateTransform();
        }
    }

    /// <summary>Gets or sets the linear velocity in units per second.</summary>
    public Vector3 Velocity { get; set; }

    /// <summary>Gets or sets the angular velocity as a QAngle in degrees per second.</summary>
    public Vector3 AngularVelocity { get; set; }

    /// <summary>
    /// Gets the time <see cref="Think"/> next runs, in <see cref="EntitySystem.CurrentTime"/> seconds,
    /// or -1 when the entity is not thinking.
    /// </summary>
    public float NextThink { get; private set; } = -1f;

    /// <summary>
    /// Gets the time <see cref="MoveDone"/> next runs, in <see cref="EntitySystem.CurrentTime"/> seconds,
    /// or -1 when no move is scheduled. Source's <c>m_flMoveDoneTime</c>, how pushing entities step their
    /// movement state machines.
    /// </summary>
    public float MoveDoneTime { get; private set; } = -1f;

    /// <summary>Gets whether this entity has been removed from the world and is awaiting cleanup.</summary>
    public bool IsRemoved { get; private set; }

    /// <summary>
    /// Gets the entity's collision shape, or <see langword="null"/> when it has none. Built by
    /// <see cref="SetModel"/> from the model's physics, and moved with the entity every tick.
    /// </summary>
    public EntityCollider? Collider { get; private set; }

    /// <summary>
    /// Gets or sets whether the entity collides with the player. Setting it to <see langword="false"/>
    /// leaves the shape built but takes the entity out of traces, which is what Source's
    /// <c>SOLID_NONE</c> amounts to here.
    /// </summary>
    public bool IsSolid { get; set; } = true;

    /// <summary>Gets whether the entity currently takes part in collision traces.</summary>
    public bool IsCollidable => IsSolid && Collider is { IsEmpty: false } && !IsRemoved;

    private readonly List<EntityChild> children = [];
    private Vector3 previousOrigin;
    private Vector3 previousAngles;
    private bool isInterpolating;

    /// <summary>Gets the scene nodes this entity spawned and drives.</summary>
    public IReadOnlyList<EntityChild> Children => children;

    /// <summary>
    /// Initializes the entity from its keyvalues, reading the properties every entity has.
    /// </summary>
    /// <param name="system">The world this entity belongs to.</param>
    /// <param name="spawnInfo">The entity's keyvalues and spawn context.</param>
    protected BaseEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system.Scene)
    {
        EntitySystem = system;
        Data = spawnInfo.Data;
        ParentTransform = spawnInfo.ParentTransform;

        Classname = Data.GetStringProperty("classname") ?? string.Empty;
        TargetName = Data.TargetName;
        SpawnFlags = Data.GetUInt32Property("spawnflags");
        EntityScale = Data.GetVector3Property("scales", Vector3.One);

        Origin = Data.GetVector3Property("origin");
        Angles = Data.GetVector3Property("angles");
        previousOrigin = Origin;
        previousAngles = Angles;

        EntityData = Data;
        LayerName = spawnInfo.LayerName;
        Name = TargetName ?? Classname;

        // The entity itself draws nothing; its children carry the geometry.
        RenderPasses = CustomRenderPasses.None;

        UpdateTransform();
    }

    /// <summary>Tests whether any of the given <c>spawnflags</c> bits are set, as Source's <c>HasSpawnFlags</c> does.</summary>
    /// <param name="flags">The bits to test.</param>
    public bool HasSpawnFlags(uint flags) => (SpawnFlags & flags) != 0;

    /// <summary>
    /// Sets up the entity: loads its model, reads class-specific keyvalues, and schedules its first think.
    /// Called by <see cref="EntityFactory"/> right after construction, before the entity enters the world.
    /// </summary>
    public virtual void Spawn()
    {
    }

    /// <summary>
    /// Called once every entity in the map has spawned, for anything that needs to resolve other entities
    /// by name. Source's <c>Activate</c>.
    /// </summary>
    public virtual void Activate()
    {
    }

    /// <summary>Runs when <see cref="NextThink"/> comes due.</summary>
    public virtual void Think()
    {
    }

    /// <summary>Runs when <see cref="MoveDoneTime"/> comes due, after the tick's movement was applied.</summary>
    public virtual void MoveDone()
    {
    }

    /// <summary>
    /// Handles an entity I/O input fired at this entity, by running the handler this entity's class
    /// declared for it with <see cref="EntityInputAttribute"/>. Override only to intercept inputs that
    /// cannot be a fixed method, and call the base to fall back to the table.
    /// </summary>
    /// <param name="inputName">The input's name, matched case-insensitively.</param>
    /// <param name="data">The parameter and the entities that sent it.</param>
    /// <returns><see langword="true"/> when the input was handled.</returns>
    public virtual bool AcceptInput(string inputName, EntityInputData data)
        => EntityInputTable.TryDispatch(this, inputName, data);

    /// <summary>Removes the entity from the world.</summary>
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("Kill")]
    protected void InputKill(EntityInputData data) => EntitySystem.Remove(this);

    /// <summary>Schedules <see cref="Think"/> to run at an absolute time; -1 stops thinking.</summary>
    /// <param name="time">Absolute time in <see cref="EntitySystem.CurrentTime"/> seconds.</param>
    public void SetNextThink(float time) => NextThink = time;

    /// <summary>
    /// Schedules <see cref="MoveDone"/> to run after a delay, matching Source's <c>SetMoveDoneTime</c>.
    /// </summary>
    /// <param name="delay">Delay in seconds, or a negative value to cancel the scheduled move.</param>
    public void SetMoveDoneTime(float delay)
        => MoveDoneTime = delay >= 0f ? EntitySystem.CurrentTime + delay : -1f;

    /// <summary>
    /// Runs one entity tick: think, move, then move-done, the order Source's pusher physics uses.
    /// </summary>
    /// <param name="tickInterval">The fixed tick length in seconds.</param>
    internal void Simulate(float tickInterval)
    {
        // The state this tick starts from is the one frames interpolate out of
        previousOrigin = Origin;
        previousAngles = Angles;

        if (NextThink > 0f && NextThink <= EntitySystem.CurrentTime)
        {
            NextThink = -1f;
            Think();
        }

        if (IsRemoved)
        {
            return;
        }

        PhysicsSimulate(tickInterval);

        if (MoveDoneTime > 0f && MoveDoneTime <= EntitySystem.CurrentTime)
        {
            MoveDoneTime = -1f;
            MoveDone();
        }
    }

    /// <summary>
    /// Integrates this tick's movement. The default advances <see cref="Origin"/> and <see cref="Angles"/>
    /// by the current velocities.
    /// </summary>
    /// <param name="tickInterval">The fixed tick length in seconds.</param>
    protected virtual void PhysicsSimulate(float tickInterval)
    {
        if (Velocity == Vector3.Zero && AngularVelocity == Vector3.Zero)
        {
            return;
        }

        var origin = Origin + Velocity * tickInterval;
        var angles = Angles + AngularVelocity * tickInterval;

        Origin = origin;
        Angles = new Vector3(AngleMod(angles.X), AngleMod(angles.Y), AngleMod(angles.Z));
    }

    /// <inheritdoc/>
    public override void Update(Scene.UpdateContext context)
    {
        var hasMoved = previousOrigin != Origin || previousAngles != Angles;

        if (hasMoved || isInterpolating)
        {
            // Once it stops moving, one last frame at the far end lands on the tick state exactly
            UpdateRenderTransform(hasMoved ? EntitySystem.InterpolationFraction : 1f);
            isInterpolating = hasMoved;
        }

        foreach (var (node, localTransform) in children)
        {
            var oldBounds = node.BoundingBox;

            node.Transform = localTransform * Transform;
            node.Update(context);

            if (node.LayerEnabled && !oldBounds.Equals(node.BoundingBox))
            {
                Scene.DynamicOctree.Update(node, oldBounds);
            }
        }
    }

    /// <summary>
    /// Loads a model and attaches its renderable and physics nodes to this entity. Source's <c>SetModel</c>.
    /// Brush entities carry their geometry this way, in a model compiled next to the map.
    /// </summary>
    /// <param name="modelName">Resource path of the model, usually the entity's <c>model</c> keyvalue.</param>
    /// <returns>The model node that was created, or <see langword="null"/> when the model has no meshes.</returns>
    protected ModelSceneNode? SetModel(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return null;
        }

        var fileLoader = EntitySystem.FileLoader;

        if (fileLoader.LoadFileCompiled(modelName)?.DataBlock is not Model model)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' failed to load model \"{Model}\"", Classname, TargetName, modelName);
            return null;
        }

        // rendercolor might sometimes be vec4, which holds renderamt
        var renderColor = Data.GetColor32Property("rendercolor");
        var renderAmount = Data.GetFloatProperty("renderamt", 1.0f);

        if (renderAmount > 1f)
        {
            renderAmount /= 255f;
        }

        var modelNode = new ModelSceneNode(Scene, model, Data.GetStringProperty("skin"))
        {
            Name = modelName,
            Tint = new Vector4(renderColor, renderAmount),
        };

        var hasMeshes = modelNode.HasMeshes;

        if (hasMeshes)
        {
            AddChild(modelNode);
        }

        if (EntityCollider.LoadPhysics(model, fileLoader) is { } physics)
        {
            Collider = new EntityCollider(physics);
            UpdateColliderTransform();

            foreach (var physicsNode in PhysSceneNode.CreatePhysSceneNodes(Scene, physics, modelName, Classname))
            {
                AddChild(physicsNode);
            }
        }

        return hasMeshes ? modelNode : null;
    }

    /// <summary>
    /// Adds a scene node this entity owns and drives. The node follows this entity's transform, with
    /// <paramref name="localTransform"/> applied in the entity's own frame.
    /// </summary>
    /// <param name="node">The node to attach.</param>
    /// <param name="localTransform">The node's transform relative to this entity.</param>
    protected void AddChild(SceneNode node, Matrix4x4? localTransform = null)
    {
        var local = localTransform ?? Matrix4x4.Identity;

        node.Parent = this;
        node.EntityData = Data;
        node.LayerName = LayerName;
        node.Transform = local * Transform;

        children.Add(new EntityChild(node, local));
        Scene.Add(node, dynamic: true);
    }

    /// <summary>
    /// Takes this entity's nodes out of the scene. Called by <see cref="EntitySystem"/> when the entity
    /// is removed from the world.
    /// </summary>
    internal void RemoveFromScene()
    {
        IsRemoved = true;

        foreach (var (node, _) in children)
        {
            Scene.Remove(node, dynamic: true);
            node.Delete();
        }

        children.Clear();
        Scene.Remove(this, dynamic: true);
    }

    /// <summary>Rebuilds <see cref="SceneNode.Transform"/> from the current scale, angles, and origin.</summary>
    protected void UpdateTransform()
    {
        SetTransform(Origin, Angles);
        UpdateColliderTransform();
    }

    /// <summary>
    /// Moves the collision shape onto the entity's current tick state.
    /// </summary>
    /// <remarks>
    /// Deliberately the tick state and not the interpolated one drawn this frame: collision answers where
    /// the entity <i>is</i>, which is the same split the engine has between a server tracing against tick
    /// state and a client drawing between snapshots. The transform is also kept rigid, leaving
    /// <see cref="EntityScale"/> out, because the shape's sweeps assume distances survive the round trip
    /// into its local space.
    /// </remarks>
    private void UpdateColliderTransform()
    {
        if (Collider == null)
        {
            return;
        }

        Collider.Transform = EntityTransformHelper.CreateRotationMatrixFromEulerAngles(Angles)
            * Matrix4x4.CreateTranslation(Origin)
            * ParentTransform;
    }

    /// <summary>
    /// Rebuilds <see cref="SceneNode.Transform"/> for drawing, somewhere between the last two ticks.
    /// </summary>
    /// <remarks>
    /// This is the engine's client-side interpolation: rather than draw the newest state, the client draws
    /// between the two most recent ones and so renders slightly in the past, which is what keeps movement
    /// smooth at any framerate. Angles go through a slerp, matching mathlib's <c>Lerp&lt;QAngle&gt;</c>,
    /// which takes the shortest arc between the two orientations. The tick state stays authoritative:
    /// interpolating here only affects what is drawn, never what is simulated.
    /// </remarks>
    /// <param name="fraction">Where the frame falls between the two ticks, from <see cref="EntitySystem.InterpolationFraction"/>.</param>
    protected virtual void UpdateRenderTransform(float fraction)
    {
        var origin = Vector3.Lerp(previousOrigin, Origin, fraction);
        var rotation = Quaternion.Slerp(
            EntityTransformHelper.CreateQuaternionFromEulerAngles(previousAngles),
            EntityTransformHelper.CreateQuaternionFromEulerAngles(Angles),
            fraction);

        Transform = Matrix4x4.CreateScale(EntityScale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(origin)
            * ParentTransform;
    }

    /// <summary>
    /// Drops the interpolation history, so the entity is drawn at its current state instead of sliding
    /// there from where it was. Source's <c>Interp_Reset</c>; call it after a teleport or any other jump
    /// that is not movement.
    /// </summary>
    protected void SnapInterpolation()
    {
        previousOrigin = Origin;
        previousAngles = Angles;
        isInterpolating = false;
        UpdateTransform();
    }

    private void SetTransform(Vector3 origin, Vector3 angles)
    {
        Transform = Matrix4x4.CreateScale(EntityScale)
            * EntityTransformHelper.CreateRotationMatrixFromEulerAngles(angles)
            * Matrix4x4.CreateTranslation(origin)
            * ParentTransform;
    }

    /// <summary>
    /// Wraps an angle into [0, 360), the way the engine's <c>anglemod</c> does, quantization included, so
    /// comparisons against a stored angle behave the same here as they do in Source.
    /// </summary>
    /// <param name="degrees">The angle in degrees.</param>
    public static float AngleMod(float degrees)
        => 360f / 65536f * ((int)(degrees * (65536f / 360f)) & 65535);

    /// <summary>Reads one component of a QAngle by index: 0 pitch, 1 yaw, 2 roll.</summary>
    /// <param name="angles">The angles to read from.</param>
    /// <param name="axis">The component index.</param>
    protected static float GetAngleAxis(Vector3 angles, int axis) => axis switch
    {
        0 => angles.X,
        1 => angles.Y,
        _ => angles.Z,
    };
}
