using System.Runtime.CompilerServices;
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
/// The base of the simulated entity hierarchy, Source's <c>CBaseEntity</c>. It carries the origin and
/// angles, ticks inside <see cref="EntitySystem"/>, and owns the scene nodes that draw it.
/// </summary>
/// <remarks>
/// Entities move on the fixed tick rather than the render frame, so think intervals and ramps match the
/// engine at any framerate. An entity is not a scene node; it owns one, <see cref="RootNode"/>, and places
/// it each frame. That node defaults to the editor box, and a class with real geometry replaces it.
/// </remarks>
public class BaseEntity
{
    /// <summary>Gets the scene this entity's nodes live in.</summary>
    public Scene Scene => EntitySystem.Scene;

    /// <summary>
    /// Gets the node this entity is drawn as, from <see cref="CreateRootNode"/>. The entity positions it;
    /// anything hanging off it follows by the scene graph's own rules.
    /// </summary>
    public SceneNode? RootNode { get; private set; }

    /// <summary>Gets the world transform the entity is drawn at, interpolated between ticks.</summary>
    public Matrix4x4 Transform { get; private set; } = Matrix4x4.Identity;

    /// <summary>Gets the visibility layer this entity's nodes belong to.</summary>
    public string? LayerName { get; }

    /// <summary>Gets the world this entity lives in.</summary>
    public EntitySystem EntitySystem { get; }

    /// <summary>
    /// Gets the entity's keyvalues as authored in the map, or <see langword="null"/> for an entity created
    /// at runtime rather than loaded from one. Use <see cref="KeyValues"/> from a class that only ever
    /// comes from a map.
    /// </summary>
    public Entity? Data { get; }

    /// <summary>
    /// Gets the map keyvalues this entity was authored with. Throws for an entity created at runtime,
    /// which has none; read <see cref="Data"/> instead in a class that can be either.
    /// </summary>
    protected Entity KeyValues => Data
        ?? throw new InvalidOperationException($"'{Classname}' was created at runtime and has no map keyvalues");

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

    /// <summary>
    /// Gets the <c>model</c> the map authored. Source's <c>m_ModelName</c>. Reading the keyvalue is generic;
    /// an entity that has something to draw applies it by deriving from <see cref="BaseModelEntity"/>.
    /// </summary>
    public string? ModelName { get; protected set; }

    /// <summary>Gets or sets the origin. Setting it rebuilds <see cref="Transform"/>.</summary>
    public Vector3 Origin
    {
        get => origin;
        set => SetOriginAndAngles(value, angles);
    }

    /// <summary>Gets or sets the orientation as a QAngle (pitch, yaw, roll) in degrees. Setting it rebuilds <see cref="Transform"/>.</summary>
    public Vector3 Angles
    {
        get => angles;
        set => SetOriginAndAngles(origin, value);
    }

    /// <summary>Gets or sets the linear velocity in units per second.</summary>
    public Vector3 Velocity { get; set; }

    /// <summary>
    /// Gets or sets the angular velocity as a QAngle in degrees per second, turning the entity about its
    /// own axes. Source's <c>SetLocalAngularVelocity</c>.
    /// </summary>
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
    /// <see cref="BaseModelEntity"/> from the model's physics, and moved with the entity every tick.
    /// </summary>
    public EntityCollider? Collider { get; protected set; }

    /// <summary>
    /// Gets or sets whether the entity collides with the player. Setting it to <see langword="false"/>
    /// leaves the shape built but takes the entity out of traces, which is what Source's
    /// <c>SOLID_NONE</c> amounts to here.
    /// </summary>
    public bool IsSolid { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the entity is a trigger volume: something passes through it and it reports
    /// the touch, rather than blocking. Source's <c>FSOLID_TRIGGER</c>.
    /// </summary>
    public bool IsTrigger { get; set; }

    /// <summary>Gets whether the entity currently takes part in collision traces.</summary>
    public bool IsCollidable => IsSolid && !IsTrigger && Collider is { IsEmpty: false } && !IsRemoved;

    /// <summary>Gets the entities currently inside this one's volume.</summary>
    public IReadOnlyCollection<BaseEntity> TouchingEntities => touching;

    private readonly HashSet<BaseEntity> touching = [];
    private readonly List<SceneNode> ownedNodes = [];
    private Vector3 origin;
    private Vector3 angles;
    private bool transformDirty = true;
    private Vector3 previousOrigin;
    private Vector3 previousAngles;
    private bool isInterpolating;

    /// <summary>
    /// Initializes the entity from its keyvalues, reading the properties every entity has.
    /// </summary>
    protected BaseEntity(EntitySystem system, EntitySpawnInfo spawnInfo)
    {
        EntitySystem = system;
        Data = spawnInfo.Data;
        ParentTransform = spawnInfo.ParentTransform;

        var data = spawnInfo.Data;

        Classname = data.GetStringProperty("classname") ?? string.Empty;
        TargetName = data.TargetName;
        SpawnFlags = data.GetUInt32Property("spawnflags");
        EntityScale = data.GetVector3Property("scales", Vector3.One);

        ModelName = data.GetStringProperty("model");

        origin = data.GetVector3Property("origin");
        angles = data.GetVector3Property("angles");
        previousOrigin = origin;
        previousAngles = angles;

        LayerName = spawnInfo.LayerName;

        UpdateTransform();

        // Last, so the override reads a fully built entity. Only the field initializers of the deriving
        // class have run by now, which is all any override here needs.
        RootNode = CreateRootNode();

        if (RootNode != null)
        {
            AddNode(RootNode);
        }
    }

    /// <summary>
    /// Initializes an entity created at runtime rather than loaded from a map, so it has no keyvalues to
    /// read and starts at the world origin.
    /// </summary>
    protected BaseEntity(EntitySystem system, string classname)
    {
        EntitySystem = system;
        ParentTransform = Matrix4x4.Identity;
        EntityScale = Vector3.One;

        Classname = classname;

        UpdateTransform();
    }

    /// <summary>
    /// Builds the node this entity is drawn as, or returns <see langword="null"/> for one that draws nothing.
    /// </summary>
    /// <remarks>
    /// The default is what the loader draws for an unimplemented classname: the icon the entity's Hammer
    /// class names, or a box in its colour. A class with real geometry overrides this, so the icon is
    /// never built for one that has geometry.
    /// </remarks>
    /// <returns>The node, or <see langword="null"/> to own none.</returns>
    protected virtual SceneNode? CreateRootNode()
    {
        if (Data == null)
        {
            return null;
        }

        // On the editor-only layer, so it hides with the other markers rather than with the world. Geometry
        // an entity really has stays on the entity's own layer.
        return World.EditorEntityNode.Create(Scene, Data, Classname, Transform);
    }

    /// <summary>
    /// Tests whether any of the given <c>spawnflags</c> bits are set, as Source's <c>HasSpawnFlags</c> does.
    /// Each entity class declares what its flags mean as a <see cref="FlagsAttribute"/> enum backed by
    /// <see cref="uint"/>.
    /// </summary>
    /// <typeparam name="TSpawnFlags">The entity class's spawnflags enum.</typeparam>
    public bool HasSpawnFlags<TSpawnFlags>(TSpawnFlags flags)
        where TSpawnFlags : struct, Enum
        => (SpawnFlags & Unsafe.BitCast<TSpawnFlags, uint>(flags)) != 0;

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
    /// Reports the box this entity occupies for touch tests, which by default is the world-space bounds of
    /// its collision shape. An entity with no shape has no volume and cannot be touched, so it says so.
    /// </summary>
    /// <remarks>
    /// A box rather than the real shape, because that is what trigger volumes test against, here and in
    /// the engine.
    /// </remarks>
    /// <returns><see langword="true"/> when this entity occupies space.</returns>
    public virtual bool TryGetTouchBounds(out Vector3 center, out Vector3 halfExtents)
    {
        if (Collider is { IsEmpty: false } collider)
        {
            var bounds = collider.WorldBounds;

            center = bounds.Center;
            halfExtents = bounds.Size * 0.5f;
            return true;
        }

        center = default;
        halfExtents = default;
        return false;
    }

    /// <summary>
    /// Whether this entity is interested in being touched by <paramref name="other"/>. A refusal keeps the
    /// touch link from opening at all, which is where a trigger's filters belong.
    /// </summary>
    protected virtual bool AcceptsTouchFrom(BaseEntity other) => true;

    /// <summary>Runs on the tick <paramref name="other"/> enters this entity's volume. Source's <c>StartTouch</c>.</summary>
    protected virtual void OnStartTouch(BaseEntity other)
    {
    }

    /// <summary>Runs every tick <paramref name="other"/> stays inside this entity's volume. Source's <c>Touch</c>.</summary>
    protected virtual void OnTouch(BaseEntity other)
    {
    }

    /// <summary>Runs on the tick <paramref name="other"/> leaves this entity's volume. Source's <c>EndTouch</c>.</summary>
    protected virtual void OnEndTouch(BaseEntity other)
    {
    }

    /// <summary>
    /// Moves the entity somewhere else outright, rather than by travelling there. Source's
    /// <c>CBaseEntity::Teleport</c>. Null angles keep the current ones.
    /// </summary>
    public virtual void Teleport(Vector3 origin, Vector3? angles)
    {
        Origin = origin;

        if (angles is { } newAngles)
        {
            Angles = newAngles;
        }

        // A teleport is not movement, so it must not be interpolated across
        SnapInterpolation();
    }

    /// <summary>
    /// Opens, sustains, or closes the touch link between this volume and <paramref name="other"/>, firing
    /// the matching handler on the edges.
    /// </summary>
    internal void UpdateTouchLink(BaseEntity other, bool isOverlapping)
    {
        if (isOverlapping && !AcceptsTouchFrom(other))
        {
            isOverlapping = false;
        }

        if (isOverlapping)
        {
            if (touching.Add(other))
            {
                OnStartTouch(other);
            }
            else
            {
                OnTouch(other);
            }
        }
        else if (touching.Remove(other))
        {
            OnEndTouch(other);
        }
    }

    /// <summary>
    /// Handles an entity I/O input fired at this entity, by running the handler this entity's class
    /// declared for it with <see cref="EntityInputAttribute"/>. Override only to intercept inputs that
    /// cannot be a fixed method, and call the base to fall back to the table.
    /// </summary>
    /// <returns><see langword="true"/> when the input was handled.</returns>
    public virtual bool AcceptInput(string inputName, EntityInputData data)
        => EntityInputTable.TryDispatch(this, inputName, data);

    /// <summary>Removes the entity from the world.</summary>
    [EntityInput("Kill")]
    protected void InputKill(EntityInputData data) => EntitySystem.Remove(this);

    /// <summary>
    /// Schedules <see cref="Think"/> to run at an absolute <see cref="EntitySystem.CurrentTime"/> in
    /// seconds; -1 stops thinking.
    /// </summary>
    public void SetNextThink(float time)
        => NextThink = time < 0f ? -1f : EntitySystem.SnapToTick(time);

    /// <summary>
    /// Schedules <see cref="MoveDone"/> to run after a delay in seconds, matching Source's
    /// <c>SetMoveDoneTime</c>. A negative delay cancels the scheduled move.
    /// </summary>
    public void SetMoveDoneTime(float delay)
        => MoveDoneTime = delay >= 0f ? EntitySystem.CurrentTime + delay : -1f;

    /// <summary>
    /// Runs one entity tick: think, move, then move-done, the order Source's pusher physics uses.
    /// </summary>
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

        // Only as far as the scheduled arrival, never past it. Source's pusher does the same
        // (physics_main.cpp: movetime is clamped to the frame), and without it a 0.1s ramp step would
        // take a whole 7th tick of movement it was never given time for.
        var moveTime = tickInterval;

        if (MoveDoneTime > 0f)
        {
            var remaining = MoveDoneTime - (EntitySystem.CurrentTime - tickInterval);

            if (remaining < moveTime)
            {
                moveTime = MathF.Max(remaining, 0f);
            }
        }

        PhysicsSimulate(moveTime);

        if (MoveDoneTime > 0f && MoveDoneTime <= EntitySystem.CurrentTime)
        {
            MoveDoneTime = -1f;
            MoveDone();
        }
    }

    /// <summary>
    /// Integrates this tick's movement, advancing <see cref="Origin"/> by <see cref="Velocity"/> and
    /// turning by <see cref="AngularVelocity"/>.
    /// </summary>
    /// <remarks>
    /// The turn goes about the entity's own axes, not onto the QAngle components. Source 1 adds the
    /// components (<c>physics_main.cpp</c>: <c>angles += GetLocalAngularVelocity() * movetime</c>), Source 2
    /// turns the body, and these are Source 2 maps. The two only differ for an entity the map already
    /// rotated, such as a brush authored on its side, which would otherwise yaw about the world's up axis.
    /// </remarks>
    protected virtual void PhysicsSimulate(float tickInterval)
    {
        if (Velocity == Vector3.Zero && AngularVelocity == Vector3.Zero)
        {
            return;
        }

        SetOriginAndAngles(
            Origin + Velocity * tickInterval,
            AngularVelocity == Vector3.Zero ? Angles : TurnBody(Angles, AngularVelocity * tickInterval));
    }

    /// <summary>
    /// Turns a QAngle by a delta given in the body's own frame, and reports where that lands as a QAngle.
    /// </summary>
    protected static Vector3 TurnBody(Vector3 from, Vector3 bodyDelta)
    {
        var turned = EntityTransformHelper.EulerAnglesToQuaternion(from)
            * EntityTransformHelper.EulerAnglesToQuaternion(bodyDelta);

        return EntityTransformHelper.ToEulerAngles(turned);
    }

    /// <summary>
    /// Moves and turns in one go, so a tick's movement rebuilds the transform once rather than once per
    /// property, and an unchanged write costs nothing.
    /// </summary>
    protected void SetOriginAndAngles(Vector3 newOrigin, Vector3 newAngles)
    {
        if (origin == newOrigin && angles == newAngles)
        {
            return;
        }

        origin = newOrigin;
        angles = newAngles;

        UpdateTransform();
    }

    /// <summary>
    /// Brings the entity's node up to date for this frame: interpolate between the last two ticks, then put
    /// the node where that lands.
    /// </summary>
    /// <remarks>
    /// The octree entry is moved here rather than in <see cref="Scene.Update"/>, which measures a node's
    /// bounds around the node's own update. This write happens before that loop runs, so it would measure
    /// no change and leave the entry stale.
    /// </remarks>
    internal void Update()
    {
        // A paused world has no span to interpolate across, and reading one would draw every entity at
        // the tick it last started rather than where it stands, re-dirtying the transform every frame
        var isMoving = EntitySystem.Enabled && (previousOrigin != origin || previousAngles != angles);

        if (isMoving || isInterpolating)
        {
            // Once it stops moving, one last frame at the far end lands on the tick state exactly
            UpdateRenderTransform(isMoving ? EntitySystem.InterpolationFraction : 1f);
            isInterpolating = isMoving;
        }

        // A still entity's nodes are already where they belong
        if (!transformDirty)
        {
            return;
        }

        transformDirty = false;

        foreach (var node in ownedNodes)
        {
            var oldBounds = node.BoundingBox;

            node.Transform = Transform;

            if (node.LayerEnabled && !oldBounds.Equals(node.BoundingBox))
            {
                Scene.DynamicOctree.Update(node, oldBounds);
            }
        }
    }

    /// <summary>
    /// Puts a node this entity owns into the scene, and takes responsibility for its lifetime and its
    /// placement. <see cref="RootNode"/> is the one the entity is drawn as; a model entity also owns the
    /// collision hulls its model was compiled with.
    /// </summary>
    protected void AddNode(SceneNode node)
    {
        node.EntityData = Data;
        node.EntityInstance = this;

        // A node that came with a layer keeps it: the editor box is built on the editor-only layer so it
        // hides with the other markers, while geometry an entity really has belongs on the entity's own
        node.LayerName ??= LayerName;
        node.Transform = Transform;

        ownedNodes.Add(node);
        Scene.Add(node, dynamic: true);
    }

    /// <summary>
    /// Takes this entity's nodes out of the scene. Called by <see cref="EntitySystem"/> when the entity
    /// is removed from the world.
    /// </summary>
    internal void RemoveFromScene()
    {
        IsRemoved = true;

        OnRemove();

        foreach (var node in ownedNodes)
        {
            node.EntityInstance = null;

            Scene.Remove(node, dynamic: true);
            node.Delete();
        }

        ownedNodes.Clear();
    }

    /// <summary>
    /// Called as the entity leaves the world, before its nodes are taken out of the scene. Source's
    /// <c>UpdateOnRemove</c>: the place to let go of anything the entity started, such as a playing sound.
    /// </summary>
    protected virtual void OnRemove()
    {
    }

    /// <summary>Rebuilds <see cref="Transform"/> from the current scale, angles, and origin.</summary>
    protected void UpdateTransform()
    {
        SetTransform(Origin, Angles);
        UpdateColliderTransform();
    }

    /// <summary>
    /// Moves the collision shape onto the entity's current tick state.
    /// </summary>
    /// <remarks>
    /// Uses the tick state, not the interpolated one drawn this frame, because collision answers where the
    /// entity is - the same split the engine has between the server tracing and the client drawing. The
    /// transform stays rigid, leaving <see cref="EntityScale"/> out, because the shape's sweeps assume
    /// distances do not change in its local space.
    /// </remarks>
    protected void UpdateColliderTransform()
    {
        if (Collider == null)
        {
            return;
        }

        Collider.Transform = EntityTransformHelper.EulerAnglesToRotationMatrix(Angles)
            * Matrix4x4.CreateTranslation(Origin)
            * ParentTransform;
    }

    /// <summary>
    /// Rebuilds <see cref="Transform"/> for drawing, somewhere between the last two ticks.
    /// </summary>
    /// <remarks>
    /// The engine's client-side interpolation: draw between the two most recent tick states instead of the
    /// newest one, which renders slightly in the past but stays smooth at any framerate. Angles slerp, like
    /// mathlib's <c>Lerp&lt;QAngle&gt;</c>, taking the shortest arc. Only what is drawn changes; the tick
    /// state stays authoritative.
    /// </remarks>
    protected virtual void UpdateRenderTransform(float fraction)
    {
        var origin = Vector3.Lerp(previousOrigin, Origin, fraction);
        var rotation = Quaternion.Slerp(
            EntityTransformHelper.EulerAnglesToQuaternion(previousAngles),
            EntityTransformHelper.EulerAnglesToQuaternion(Angles),
            fraction);

        Transform = Matrix4x4.CreateScale(EntityScale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(origin)
            * ParentTransform;

        transformDirty = true;
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
            * EntityTransformHelper.EulerAnglesToRotationMatrix(angles)
            * Matrix4x4.CreateTranslation(origin)
            * ParentTransform;

        transformDirty = true;
    }

    /// <summary>
    /// Wraps an angle into [0, 360), the way the engine's <c>anglemod</c> does, quantization included, so
    /// comparisons against a stored angle behave the same here as they do in Source.
    /// </summary>
    public static float AngleMod(float degrees)
        => 360f / 65536f * ((int)(degrees * (65536f / 360f)) & 65535);
}
