using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using Entity = ValveResourceFormat.ResourceTypes.EntityLump.Entity;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Everything <see cref="EntityFactory"/> needs to bring an entity into the world: its keyvalues, the
/// transform of whatever spawned it, the visibility layer its scene nodes belong to, and the scene those
/// nodes go into.
/// </summary>
/// <param name="Data">The entity's keyvalues, as authored in the map.</param>
/// <param name="ParentTransform">Transform of the spawner (a template or spawn group placement, identity for map entities), applied to the authored origin and angles.</param>
/// <param name="LayerName">Visibility layer for this entity and every node it creates.</param>
/// <param name="Scene">The scene the entity's nodes render into.</param>
public readonly record struct EntitySpawnInfo(Entity Data, Matrix4x4 ParentTransform, string? LayerName, Scene Scene);

/// <summary>
/// The base of the simulated entity hierarchy, Source's <c>CBaseEntity</c>. It carries the origin and
/// angles, ticks inside <see cref="EntitySystem"/>, and owns the scene nodes that draw it.
/// </summary>
/// <remarks>
/// Entities move on the fixed tick rather than the render frame, so think intervals and ramps match the
/// engine at any framerate. An entity is not a scene node; it owns one, <see cref="RootNode"/>, and places
/// it each frame. That node defaults to the editor box, and a class with real geometry replaces it.
/// </remarks>
public abstract class BaseEntity
{
    /// <summary>Gets the scene that holds the nodes of this entity.</summary>
    public Scene Scene { get; }

    /// <summary>
    /// Gets the node this entity is drawn as, from <see cref="CreateRootNode"/>. The entity positions it;
    /// anything hanging off it follows by the scene graph's own rules.
    /// </summary>
    public SceneNode? RootNode { get; private set; }

    /// <summary>Gets the world transform the entity is drawn at, interpolated between ticks.</summary>
    public Matrix4x4 Transform { get; private set; } = Matrix4x4.Identity;

    /// <summary>
    /// Gets where the entity is in the world at its current tick, without its <see cref="EntityScale"/>:
    /// the placement of anything the scale must not stretch, such as collision or a baked volume.
    /// </summary>
    public Matrix4x4 RigidTransform => EntityTransformHelper.ToRigidTransformationMatrix(angles, origin) * GetParentFrame();

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

    /// <summary>
    /// Gets the placement of whatever spawned this entity, a template or a spawn group, already applied to its
    /// pose. Identity for plain map entities.
    /// </summary>
    public Matrix4x4 SpawnTransform { get; }

    /// <summary>
    /// Gets or sets the owning entity, Source's <c>m_hOwnerEntity</c>. Null only on the root <see cref="WorldEntity"/>.
    /// </summary>
    public BaseEntity? Owner { get; set; }

    /// <summary>
    /// Gets the entity this one moves with, the map's <c>parentname</c> - the move parent, which is a
    /// different link than <see cref="Owner"/>. A door's handle rides its door through this.
    /// </summary>
    public BaseEntity? MoveParent { get; private set; }

    /// <summary>
    /// Gets whether <see cref="ResolveMoveParent"/> has run and whether
    /// empty MoveParent means it has not yet been resolved or there's no parent.
    /// </summary>
    internal bool IsMoveParentResolved { get; private set; }

    // What the move parent frame is: the parent entity, or an attachment point or bone of its model
    private enum ParentFrameKind
    {
        None,
        Entity,
        Attachment,
    }

    private ParentFrameKind parentFrameKind;
    private string? parentAttachmentName;

    // Entities whose pose is relative to this one
    private readonly List<BaseEntity> moveChildren = [];

    // What blocked the last push, Source's m_pBlocker
    private BaseEntity? currentBlocker;

    // Taken out of traces while its own push is checked, Source's UnlinkPusherList
    private bool isCollisionSuspended;

    // Source's sv_stepsize, which pads the push's search volume above the pushers
    private const float PushStepSize = 18f;

    /// <summary>
    /// Resolves <c>parentname</c> once everything has spawned, and turns the authored world pose into one
    /// local to the parent, the way the engine does at spawn.
    /// </summary>
    internal void ResolveMoveParent()
    {
        IsMoveParentResolved = true;

        var parentName = Data?.GetStringProperty("parentname");

        if (string.IsNullOrEmpty(parentName))
        {
            return;
        }

        var attachmentName = Data?.GetStringProperty("parentattachmentname");

        // "name,attachment" addresses an attachment point as part of the parent
        var comma = parentName.IndexOf(',', StringComparison.Ordinal);

        if (comma >= 0)
        {
            if (string.IsNullOrEmpty(attachmentName))
            {
                attachmentName = parentName[(comma + 1)..];
            }

            parentName = parentName[..comma];
        }

        foreach (var candidate in EntitySystem.FindAllByTargetName(parentName, Scene))
        {
            if (candidate != this)
            {
                MoveParent = candidate;
                break;
            }
        }

        if (MoveParent == null)
        {
            return;
        }

        // Its world pose has to be final before this one is made relative to it
        EntitySystem.ResolveMoveParentChain(MoveParent);

        var data = Data!;
        var useLocalOffset = data.GetBooleanProperty("uselocaloffset");
        var authoredLocalOrigin = data.GetVector3Property("local.origin");
        var authoredLocalAngles = data.GetVector3Property("local.angles");

        if (!string.IsNullOrEmpty(attachmentName) && attachmentName != "!absorigin"
            && MoveParent is BaseModelEntity { ModelNode: { } parentModel } && parentModel.HasAttachmentOrBone(attachmentName))
        {
            parentFrameKind = ParentFrameKind.Attachment;
            parentAttachmentName = attachmentName;

            // Snapped onto the attachment unless an offset from it was authored
            if (useLocalOffset)
            {
                SetPose(authoredLocalOrigin, authoredLocalAngles);
            }
            else
            {
                SetPose(Vector3.Zero, Vector3.Zero);
            }

            AttachToParentModel(parentModel);
        }
        else
        {
            if (!string.IsNullOrEmpty(attachmentName) && attachmentName != "!absorigin")
            {
                EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' is parented to {AttachmentName} on '{ParentName}', which has no such attachment or bone",
                    Classname, TargetName, attachmentName, MoveParent.TargetName);
            }

            var world = RigidTransform;

            parentFrameKind = ParentFrameKind.Entity;

            if (useLocalOffset)
            {
                SetPose(authoredLocalOrigin, authoredLocalAngles);
            }
            else if (!data.GetBooleanProperty("positioninlocalspace"))
            {
                // Keeps the authored world pose, now relative to the parent
                SetWorldPose(world);
            }
        }

        MoveParent.moveChildren.Add(this);

        SnapInterpolation();
    }

    /// <summary>Gets the authored <c>scales</c>, which movement never changes.</summary>
    public Vector3 EntityScale { get; }

    /// <summary>
    /// Gets the <c>model</c> the map authored. Source's <c>m_ModelName</c>. Reading the keyvalue is generic;
    /// an entity that has something to draw applies it by deriving from <see cref="BaseModelEntity"/>.
    /// </summary>
    public string? ModelName { get; protected set; }

    /// <summary>
    /// Gets or sets the origin relative to the move parent frame, the world origin for an entity without one.
    /// Source's <c>m_vecOrigin</c>, which movement works in. Setting it rebuilds <see cref="Transform"/>.
    /// </summary>
    public Vector3 Origin
    {
        get => origin;
        set => SetOriginAndAngles(value, angles);
    }

    /// <summary>
    /// Gets or sets the orientation relative to the move parent frame, as a QAngle (pitch, yaw, roll) in
    /// degrees. Source's <c>m_angRotation</c>. Setting it rebuilds <see cref="Transform"/>.
    /// </summary>
    public Vector3 Angles
    {
        get => angles;
        set => SetOriginAndAngles(origin, value);
    }

    /// <summary>Gets or sets the world origin at the current tick. Setting it moves the local origin to match.</summary>
    public Vector3 WorldOrigin
    {
        get => parentFrameKind == ParentFrameKind.None ? origin : RigidTransform.Translation;
        set => SetWorldOriginAndAngles(value, WorldAngles);
    }

    /// <summary>Gets or sets the world orientation at the current tick. Setting it turns the local angles to match.</summary>
    public Vector3 WorldAngles
    {
        get => parentFrameKind == ParentFrameKind.None
            ? angles
            : EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(RigidTransform));
        set => SetWorldOriginAndAngles(WorldOrigin, value);
    }

    /// <summary>Gets or sets the linear velocity in units per second, in the move parent frame.</summary>
    public Vector3 Velocity { get; set; }

    /// <summary>
    /// Gets or sets the angular velocity as a QAngle in degrees per second, turning the entity about its
    /// own axes, or through its angle components where <see cref="TurnsByAngleComponents"/> says so.
    /// Source's <c>SetLocalAngularVelocity</c>.
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

    /// <summary>
    /// Gets or sets whether the entity's geometry is drawn, Source's <c>EF_NODRAW</c>. Every node it
    /// owns follows, through <see cref="SceneNode.Visible"/>, so toggling costs nothing.
    /// </summary>
    public bool IsDrawn
    {
        get;
        set
        {
            field = value;

            foreach (var node in ownedNodes)
            {
                node.Visible = value;
            }
        }
    } = true;

    /// <summary>Gets whether the entity currently takes part in collision traces.</summary>
    public bool IsCollidable => IsSolid && !IsTrigger && Collider is { IsEmpty: false } && !IsRemoved && !isCollisionSuspended;

    /// <summary>Gets the entities currently inside this one's volume.</summary>
    public IReadOnlyCollection<BaseEntity> TouchingEntities => touching;

    private readonly HashSet<BaseEntity> touching = [];
    private readonly List<SceneNode> ownedNodes = [];

    // The owned nodes the entity also places; the rest are placed by something else
    private readonly List<SceneNode> placedNodes = [];
    private Vector3 origin;
    private Vector3 angles;
    private bool transformDirty = true;
    private Vector3 previousOrigin;
    private Vector3 previousAngles;
    private bool isInterpolating;

    // Transform without the scale, where children following this entity are drawn from
    private Matrix4x4 renderFrame = Matrix4x4.Identity;

    /// <summary>
    /// Initializes the entity from its keyvalues, reading the properties every entity has.
    /// </summary>
    protected BaseEntity(EntitySystem system, EntitySpawnInfo spawnInfo)
    {
        EntitySystem = system;
        Scene = spawnInfo.Scene;
        Data = spawnInfo.Data;
        SpawnTransform = spawnInfo.ParentTransform;
        var data = spawnInfo.Data;

        Classname = data.GetStringProperty("classname") ?? string.Empty;
        TargetName = data.TargetName;
        SpawnFlags = data.GetUInt32Property("spawnflags");
        EntityScale = data.GetVector3Property("scales", Vector3.One);

        ModelName = data.GetStringProperty("model");

        // Authored in the world of the map they were compiled in, and moved with whatever placed it
        var authored = EntityTransformHelper.ToRigidTransformationMatrix(data.GetVector3Property("angles"), data.GetVector3Property("origin"))
            * spawnInfo.ParentTransform;

        origin = authored.Translation;
        angles = spawnInfo.ParentTransform.IsIdentity
            ? data.GetVector3Property("angles")
            : EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(authored));
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
    /// <param name="system">The entity world it lives in.</param>
    /// <param name="scene">The scene its nodes render into.</param>
    /// <param name="classname">The classname it reports.</param>
    protected BaseEntity(EntitySystem system, Scene scene, string classname)
    {
        EntitySystem = system;
        Scene = scene;
        EntityScale = Vector3.One;
        SpawnTransform = Matrix4x4.Identity;

        Classname = classname;

        UpdateTransform();
    }

    /// <summary>
    /// Builds the node this entity is drawn as, or returns <see langword="null"/> for one that draws nothing.
    /// </summary>
    /// <remarks>
    /// The default is the editor marker, <see cref="CreateEditorNode"/>: the icon the entity's Hammer class
    /// names, or a box in its colour. A class with real geometry overrides this, so the icon is never built
    /// for one that has geometry.
    /// </remarks>
    /// <returns>The node, or <see langword="null"/> to own none.</returns>
    protected virtual SceneNode? CreateRootNode() => CreateEditorNode();

    /// <summary>
    /// Builds the node the editor draws this entity as: the icon its Hammer class names, or a box in its
    /// colour. <see langword="null"/> for an entity created at runtime, which has no Hammer class.
    /// </summary>
    /// <param name="flags">Flags for the node.</param>
    /// <returns>The node, or <see langword="null"/>.</returns>
    protected SceneNode? CreateEditorNode(ObjectTypeFlags flags = ObjectTypeFlags.None)
    {
        if (Data == null)
        {
            return null;
        }

        // On the editor-only layer, so it hides with the other markers rather than with the world, except a
        // template and what it spawns, which are grouped together. An icon the Hammer class draws as a
        // studio model stands in for real geometry, so it stays on the entity's own layer.
        var layerName = LayerName == World.EditorEntityNode.TemplateLayerName
            ? World.EditorEntityNode.TemplateLayerName
            : HammerEntities.Get(Classname)?.Studio == true && LayerName != null
                ? LayerName
                : World.EditorEntityNode.LayerName;

        return World.EditorEntityNode.Create(Scene, Data, Classname, Transform, RigidTransform, flags, layerName);
    }

    /// <summary>
    /// Loads an effect for the entity to play, at the entity and on the particles layer. The caller decides
    /// whether the entity owns and places it, through <see cref="AddNode"/>.
    /// </summary>
    /// <param name="effectName">The effect, or <see langword="null"/> or empty for none.</param>
    /// <param name="snapshot">A snapshot the effect starts from, such as a rope's points.</param>
    /// <param name="playedByEntity">Whether the entity sets the control points, rather than the effect's own configuration.</param>
    /// <returns>The effect, or <see langword="null"/> when there is none or it failed to load.</returns>
    protected ParticleSceneNode? CreateEffect(string? effectName, ParticleSnapshot? snapshot = null, bool playedByEntity = true)
    {
        if (string.IsNullOrEmpty(effectName))
        {
            return null;
        }

        if (EntitySystem.FileLoader.LoadFileCompiled(effectName)?.DataBlock is not ParticleSystem particleSystem)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' failed to load effect \"{Effect}\"", Classname, TargetName, effectName);
            return null;
        }

        try
        {
            return new ParticleSceneNode(Scene, particleSystem, snapshot, playedByEntity: playedByEntity)
            {
                Name = effectName,
                Transform = Transform,
                LayerName = Scene.ParticlesLayerName,
            };
        }
        catch (Exception e)
        {
            EntitySystem.Logger.LogError(e, "{Classname} '{TargetName}' failed to set up effect \"{Effect}\"", Classname, TargetName, effectName);
            return null;
        }
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

    /// <summary>
    /// Runs when the host declares a round started, via <see cref="EntitySystem.StartRound"/>. What the
    /// engine's game rules announce to entities on round restart.
    /// </summary>
    public virtual void RoundStart()
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

    /// <summary>
    /// Runs every tick <paramref name="other"/> is inside this entity's volume, the tick it entered included,
    /// and on a tick the player ran into this solid entity while moving. Source's <c>Touch</c>, which serves both.
    /// </summary>
    protected virtual void OnTouch(BaseEntity other)
    {
    }

    /// <summary>Runs on the tick <paramref name="other"/> leaves this entity's volume. Source's <c>EndTouch</c>.</summary>
    protected virtual void OnEndTouch(BaseEntity other)
    {
    }

    /// <summary>
    /// Gets what this entity can do, which is how the player's use trace decides whether it is worth
    /// pressing. Source's <c>ObjectCaps</c>.
    /// </summary>
    public virtual EntityCapability ObjectCaps => EntityCapability.None;

    /// <summary>
    /// Runs when something presses this entity. Source's <c>CBaseEntity::Use</c>, minus the use type and
    /// value, which nothing here distinguishes.
    /// </summary>
    public virtual void Use(BaseEntity? activator)
    {
    }

    /// <summary>
    /// Moves the entity somewhere else outright, rather than by travelling there. Source's
    /// <c>CBaseEntity::Teleport</c>. Null angles keep the current ones.
    /// </summary>
    public virtual void Teleport(Vector3 origin, Vector3? angles)
    {
        SetWorldOriginAndAngles(origin, angles ?? WorldAngles);

        // A teleport is not movement, so it must not be interpolated across
        SnapInterpolation();
    }

    /// <summary>Jumps to a pose in the move parent frame without interpolating across, like a teleport.</summary>
    protected void JumpTo(Vector3 origin, Vector3 angles)
    {
        SetOriginAndAngles(origin, angles);
        SnapInterpolation();
    }

    /// <summary>Reports that <paramref name="other"/> ran into this solid entity, as a touch.</summary>
    internal void Impact(BaseEntity other) => OnTouch(other);

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
            // The engine touches on the tick of entry too, straight after the start, so a trigger_multiple
            // fires as the player walks in rather than a tick later
            if (touching.Add(other))
            {
                OnStartTouch(other);
            }

            if (!IsRemoved && !other.IsRemoved)
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

    /// <summary>Fires the <c>OnUser1</c> output; every entity answers <c>FireUser1</c>.</summary>
    [EntityInput("FireUser1")]
    protected void InputFireUser1(EntityInputData data) => EntitySystem.TriggerOutput(this, "OnUser1", data.Activator);

    /// <summary>Fires the <c>OnUser2</c> output.</summary>
    [EntityInput("FireUser2")]
    protected void InputFireUser2(EntityInputData data) => EntitySystem.TriggerOutput(this, "OnUser2", data.Activator);

    /// <summary>Fires the <c>OnUser3</c> output.</summary>
    [EntityInput("FireUser3")]
    protected void InputFireUser3(EntityInputData data) => EntitySystem.TriggerOutput(this, "OnUser3", data.Activator);

    /// <summary>Fires the <c>OnUser4</c> output.</summary>
    [EntityInput("FireUser4")]
    protected void InputFireUser4(EntityInputData data) => EntitySystem.TriggerOutput(this, "OnUser4", data.Activator);

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
    // The state this tick starts from is the one frames interpolate out of. Taken for every entity before
    // any of them moves, so a child sees where its parent started.
    internal void BeginTick()
    {
        previousOrigin = origin;
        previousAngles = angles;
    }

    // Whether the entity moved in the world this tick, by itself or by riding its move parent
    private bool MovedThisTick()
        => previousOrigin != origin || previousAngles != angles
        || (parentFrameKind == ParentFrameKind.Entity && MoveParent!.MovedThisTick());

    internal void Simulate(float tickInterval)
    {
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

        // Only for its own motion, as what it rode was pushed with the parent already
        if (IsPusher && !MovesWithoutPushing && (previousOrigin != origin || previousAngles != angles))
        {
            UpdateBlocker(PushPlayer());
        }

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
    /// Source 2 does it both ways, chosen per class by <see cref="TurnsByAngleComponents"/>. By default the
    /// turn goes about the entity's own axes, so a brush authored on its side spins about its own up axis
    /// rather than the world's. The doors and other toggle brushes instead add the rate onto the QAngle
    /// components, as Source 1 always did. The two only differ for an entity the map already rotated.
    /// </remarks>
    protected virtual void PhysicsSimulate(float tickInterval)
    {
        if (Velocity == Vector3.Zero && AngularVelocity == Vector3.Zero)
        {
            return;
        }

        var turn = AngularVelocity * tickInterval;

        SetOriginAndAngles(
            origin + Velocity * tickInterval,
            AngularVelocity == Vector3.Zero ? angles
                : TurnsByAngleComponents ? angles + turn
                : TurnBody(angles, turn));
    }

    /// <summary>
    /// Gets whether a turn adds <see cref="AngularVelocity"/> onto the QAngle components rather than turning
    /// the body about its own axes. The doors and other toggle brushes do, so their travel between two
    /// authored angles is a straight line through the components and lands where it was aimed.
    /// </summary>
    protected virtual bool TurnsByAngleComponents => false;

    /// <summary>
    /// Gets whether this entity pushes the player out of its way as it moves, Source's
    /// <c>MOVETYPE_PUSH</c>. Doors, buttons, rotating brushes and trains opt in.
    /// </summary>
    protected internal virtual bool IsPusher => false;

    /// <summary>
    /// Gets whether the player can never block this pusher, Source's <c>FL_UNBLOCKABLE_BY_PLAYER</c>. The
    /// player is moved the whole way, through the world if need be, and the pusher keeps going.
    /// </summary>
    protected virtual bool IsUnblockableByPlayer => false;

    /// <summary>
    /// Gets or sets whether this pusher moves through whatever is in its way without pushing it or being
    /// blocked, the <c>MoveWithoutPushingBlockers</c> attribute.
    /// </summary>
    protected bool MovesWithoutPushing { get; set; }

    /// <summary>
    /// Gets whether a player in the way of anything parented under this entity is pushed the way the game
    /// pushes them off trains: slid along the push and lifted out of whatever it left them in, and never
    /// able to block it.
    /// </summary>
    protected virtual bool PushesPlayerAsTrain => false;

    /// <summary>
    /// Gets whether this entity collides as a physics mesh, Source's <c>SOLID_VPHYSICS</c>, as nearly every
    /// model does. A turning one pushes the player by the motion of the corner of their box that leads
    /// into the turn, rather than by the motion of their origin.
    /// </summary>
    protected virtual bool HasVPhysicsSolid => true;

    /// <summary>
    /// The engine's pusher physics, run on the tick right after this entity's own move. Everything
    /// parented to the pusher moves with it as one body. A player standing on any of it, or that the
    /// new pose overlaps, is moved by the whole push, cut short only by whatever else is in the way. When
    /// that leaves them inside something, they block the pusher, which takes its motion back.
    /// </summary>
    /// <returns>What blocked the push, or <see langword="null"/>.</returns>
    private PlayerEntity? PushPlayer()
    {
        if (EntitySystem.Player is not { IsRemoved: false } player
            || !player.Controller.IsActive
            || Scene != player.Scene)
        {
            return null;
        }

        var controller = player.Controller;
        var center = controller.HullCenter;
        var halfExtents = controller.HullHalfExtents;

        List<BaseEntity> pushers = [];
        CollectPushers(pushers);

        // A turn is pushed as one even when the origin moves too, and the search volume only covers
        // what the origin's own travel swept
        var motion = GetTickMotion(out var current);
        var rotational = previousAngles != angles;
        var sweep = DisplacementAt(motion, current.Translation);

        if (!IsInPushersWay(pushers, controller.GroundEntity, center, halfExtents, sweep))
        {
            return null;
        }

        var push = rotational ? RotationalPushAt(motion, center, halfExtents) : DisplacementAt(motion, center);
        Vector3 moved;

        if (RootMoveParent.PushesPlayerAsTrain)
        {
            moved = PushPlayerAsTrain(controller, pushers, center, push, rotational);
        }
        else if (!TrySpeculativePush(controller, pushers, center, push, rotational, out moved))
        {
            SetOriginAndAngles(previousOrigin, previousAngles);

            // The engine measures the arrival on the entity's own clock, which a blocked tick winds back, so
            // the arrival slips by the whole tick. Moving it by only the clamped final step can land it in
            // the past, where a negative delay would cancel the arrival and leave the entity travelling forever.
            if (MoveDoneTime > 0f)
            {
                MoveDoneTime += EntitySystem.TickInterval;
            }

            return player;
        }

        controller.Push(moved - center);
        return null;
    }

    // This entity and everything parented under it, Source's SetupAllInHierarchy
    private void CollectPushers(List<BaseEntity> pushers)
    {
        pushers.Add(this);

        foreach (var child in moveChildren)
        {
            child.CollectPushers(pushers);
        }
    }

    private BaseEntity RootMoveParent
    {
        get
        {
            var root = this;

            while (root.MoveParent is { } parent)
            {
                root = parent;
            }

            return root;
        }
    }

    /// <summary>
    /// Whether the player is something this push has to move, Source's <c>GenerateBlockingEntityList</c>:
    /// anyone standing on the pushers rides them, anyone else only when the new pose overlaps them. Both
    /// only within the volume the pushers now fill, stretched back over the ground the push covered and
    /// two steps up.
    /// </summary>
    private static bool IsInPushersWay(List<BaseEntity> pushers, BaseEntity? ground, Vector3 center, Vector3 halfExtents, Vector3 sweep)
    {
        AABB? filled = null;

        foreach (var pusher in pushers)
        {
            if (pusher.IsCollidable)
            {
                var bounds = pusher.Collider!.WorldBounds;
                filled = filled?.Union(bounds) ?? bounds;
            }
        }

        if (filled is not { } volume || !(volume.Min.X < volume.Max.X && volume.Min.Y < volume.Max.Y && volume.Min.Z < volume.Max.Z))
        {
            return false;
        }

        var searched = new AABB(
            volume.Min - Vector3.Max(sweep, Vector3.Zero),
            volume.Max - Vector3.Min(sweep, Vector3.Zero) + new Vector3(0f, 0f, 2f * PushStepSize));

        if (!searched.Intersects(new AABB(center - halfExtents, center + halfExtents)))
        {
            return false;
        }

        if (ground != null && pushers.Contains(ground))
        {
            return true;
        }

        // Shrunk like the movement code's own overlap probes: the SAT test is exact, and a hull resting
        // its SurfaceEpsilon gap away reads as touching at times, which would jitter false pushes
        var probeExtents = halfExtents - new Vector3(Rubikon.SurfaceEpsilon / 2f);

        foreach (var pusher in pushers)
        {
            if (pusher.IsCollidable && pusher.Collider!.OverlapsVolume(center, probeExtents))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Where this tick's turn takes the player, Source's <c>ComputeRotationalPushDirection</c>: the motion
    /// of their origin at the feet, or for a physics mesh pusher, of the corner of their box that leads
    /// into that motion.
    /// </summary>
    private Vector3 RotationalPushAt(in Matrix4x4 motion, Vector3 center, Vector3 halfExtents)
    {
        var start = center with { Z = center.Z - halfExtents.Z };
        var move = DisplacementAt(motion, start);

        if (HasVPhysicsSolid)
        {
            var min = center - halfExtents;
            var max = center + halfExtents;

            start = new Vector3(
                move.X < 0f ? max.X : min.X,
                move.Y < 0f ? max.Y : min.Y,
                move.Z < 0f ? max.Z : min.Z);

            move = DisplacementAt(motion, start);
        }

        return move;
    }

    /// <summary>
    /// Moves the player by the push, Source's <c>SpeculativelyCheckPush</c>. The move is traced with the
    /// pushers out of the way and stops at whatever else is there. A straight push that went the whole
    /// way is done; otherwise the player must have come out clear of everything, pushers included.
    /// </summary>
    /// <returns><see langword="false"/> when the player blocks the push.</returns>
    private bool TrySpeculativePush(IPlayerController controller, List<BaseEntity> pushers, Vector3 center, Vector3 push, bool rotational, out Vector3 moved)
    {
        var destination = center + push;
        var trace = TraceWithPushersUnlinked(controller, pushers, center, destination);

        EntitySystem.NotePlayerImpact(trace);

        if (!IsUnblockableByPlayer)
        {
            moved = trace.Hit ? trace.HitPosition : destination;

            // A straight push that went the whole way cannot have left them inside anything
            return (!rotational && !trace.Hit) || !controller.IsHullStuck(moved);
        }

        moved = destination;

        if (!controller.IsHullStuck(destination))
        {
            return true;
        }

        // Nudged half a unit either way along the pusher's forward and left axes to shed accumulated
        // error, and left inside if none of those clears
        var transform = RigidTransform;
        Span<Vector3> axes =
        [
            new(transform.M11, transform.M12, transform.M13),
            new(transform.M21, transform.M22, transform.M23),
        ];

        for (var i = 0; i < 4; i++)
        {
            var nudged = destination + axes[i >> 1] * ((i & 1) == 0 ? 0.5f : -0.5f);

            if (!controller.IsHullStuck(nudged))
            {
                moved = nudged;
                return true;
            }
        }

        return true;
    }

    /// <summary>
    /// The game's own push for a player in a train's way, which never blocks. A rider is carried like by
    /// any pusher, then lifted out of whatever that left them in; anyone else is slid along the push.
    /// </summary>
    private Vector3 PushPlayerAsTrain(IPlayerController controller, List<BaseEntity> pushers, Vector3 center, Vector3 push, bool rotational)
    {
        var groundRoot = controller.GroundEntity?.RootMoveParent;
        var moved = center;
        var direction = Vector3.UnitZ;
        float distance;

        if (groundRoot == RootMoveParent)
        {
            TrySpeculativePush(controller, pushers, center, push, rotational, out moved);
            distance = LiftOutDistance(controller, moved);
        }
        else
        {
            distance = push.Length();
            direction = distance > 0f ? push / distance : Vector3.Zero;
        }

        // Slid again from where that left them while they are still inside something: a turn up to three
        // more times, a straight push once more unless they stand on this very pusher
        var slides = rotational ? 4 : groundRoot != this ? 2 : 1;

        for (var slide = 0; slide < slides; slide++)
        {
            if (slide > 0 && !controller.IsHullStuck(moved))
            {
                break;
            }

            moved = SlidePlayer(controller, pushers, moved, direction, distance);
        }

        return moved;
    }

    /// <summary>
    /// How far a carried train rider must be lifted: nothing while they are clear, else 1.1 times the
    /// height of whatever a hull dropped from 72 units above them lands on.
    /// </summary>
    private static float LiftOutDistance(IPlayerController controller, Vector3 center)
    {
        if (!controller.IsHullStuck(center))
        {
            return 0f;
        }

        var trace = controller.TraceHull(center + new Vector3(0f, 0f, 72f), center);

        return trace.Hit ? (trace.HitPosition.Z - center.Z) * 1.1f : 0f;
    }

    /// <summary>
    /// Slides the player along a push for up to four bumps, with the pushers out of the way. Every sweep
    /// starts four units above where the slide began. A bump takes the push off the surface it hit and
    /// lengthens what remains by as much as the hit was a graze.
    /// </summary>
    private Vector3 SlidePlayer(IPlayerController controller, List<BaseEntity> pushers, Vector3 center, Vector3 direction, float distance)
    {
        var start = center + new Vector3(0f, 0f, 4f);
        var moved = center;
        var trace = new Rubikon.TraceResult();

        for (var bump = 0; bump < 4; bump++)
        {
            var end = moved + direction * distance;
            trace = TraceWithPushersUnlinked(controller, pushers, start, end);

            if (!trace.Hit)
            {
                moved = end;
                break;
            }

            var length = Vector3.Distance(start, end);
            var fraction = length > 0f ? MathF.Min(trace.Distance / length, 1f) : 0f;

            if (fraction > 0f)
            {
                moved = trace.HitPosition;
            }

            var normal = trace.HitNormal;
            var into = Vector3.Dot(direction, normal);

            distance = (2f - MathF.Abs(into)) * ((1f - fraction) * distance);
            direction = MathUtils.ProjectOntoPlane(direction, normal);

            var back = Vector3.Dot(direction, normal);

            if (back < 0f)
            {
                direction -= normal * back;
            }
        }

        EntitySystem.NotePlayerImpact(trace);
        return moved;
    }

    // Sweeps the hull with the pushed hierarchy taken out of the world, as its own new pose is not what
    // the player is being moved out of
    private static Rubikon.TraceResult TraceWithPushersUnlinked(IPlayerController controller, List<BaseEntity> pushers, Vector3 from, Vector3 to)
    {
        SetCollisionSuspended(pushers, true);
        var trace = controller.TraceHull(from, to);
        SetCollisionSuspended(pushers, false);

        return trace;
    }

    private static void SetCollisionSuspended(List<BaseEntity> entities, bool suspended)
    {
        foreach (var entity in entities)
        {
            entity.isCollisionSuspended = suspended;
        }
    }

    // Source's pusher remembers what blocked it, telling the entity when that changes and then on every
    // blocked tick
    private void UpdateBlocker(BaseEntity? blocker)
    {
        if (blocker != currentBlocker)
        {
            if (currentBlocker != null)
            {
                OnEndBlocked();
            }

            currentBlocker = blocker;

            if (blocker != null)
            {
                OnStartBlocked(blocker);
            }
        }

        if (blocker != null)
        {
            OnBlocked(blocker);
        }
    }

    /// <summary>Runs on the first tick a push is blocked by <paramref name="blocker"/>. Source's <c>StartBlocked</c>.</summary>
    /// <param name="blocker">What the push could not move.</param>
    protected virtual void OnStartBlocked(BaseEntity blocker)
    {
    }

    /// <summary>
    /// Runs on every tick a push is blocked, after the tick's motion was taken back. Source's <c>Blocked</c>.
    /// </summary>
    /// <param name="blocker">What the push could not move.</param>
    protected virtual void OnBlocked(BaseEntity blocker)
    {
    }

    /// <summary>Runs on the first push that is no longer blocked. Source's <c>EndBlocked</c>.</summary>
    protected virtual void OnEndBlocked()
    {
    }

    /// <summary>
    /// This tick's own motion as one transform, taking where a world point was to where it is now. The
    /// move parent's motion is left out, as the parent's push already carried the player with it.
    /// </summary>
    private Matrix4x4 GetTickMotion(out Matrix4x4 current)
    {
        var parentFrame = GetParentFrame();
        var previous = EntityTransformHelper.ToRigidTransformationMatrix(previousAngles, previousOrigin) * parentFrame;

        current = EntityTransformHelper.ToRigidTransformationMatrix(angles, origin) * parentFrame;

        return Matrix4x4.Invert(previous, out var previousToLocal) ? previousToLocal * current : Matrix4x4.Identity;
    }

    // Where the tick's motion took a world point, minus where it was: the rigid displacement
    private static Vector3 DisplacementAt(in Matrix4x4 motion, Vector3 point) => Vector3.Transform(point, motion) - point;

    /// <summary>The velocity of this entity's surface at a world position: linear plus the angular sweep.</summary>
    public Vector3 GetSurfaceVelocity(Vector3 at)
    {
        var omega = new Vector3(AngularVelocity.Z, AngularVelocity.X, AngularVelocity.Y) * (MathF.PI / 180f);

        return Velocity + Vector3.Cross(omega, at - WorldOrigin);
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

    /// <summary>Puts the entity at a world origin and orientation, whatever frame it moves in.</summary>
    protected void SetWorldOriginAndAngles(Vector3 newOrigin, Vector3 newAngles)
    {
        if (parentFrameKind == ParentFrameKind.None)
        {
            SetOriginAndAngles(newOrigin, newAngles);
            return;
        }

        SetWorldPose(EntityTransformHelper.ToRigidTransformationMatrix(newAngles, newOrigin));
    }

    private void SetWorldPose(in Matrix4x4 world)
    {
        if (!Matrix4x4.Invert(GetParentFrame(), out var worldToParent))
        {
            return;
        }

        var local = world * worldToParent;

        SetPose(local.Translation, EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(local)));
    }

    private void SetPose(Vector3 newOrigin, Vector3 newAngles) => SetOriginAndAngles(newOrigin, newAngles);

    // The frame the local pose is in at the current tick
    private Matrix4x4 GetParentFrame() => parentFrameKind switch
    {
        ParentFrameKind.Entity => MoveParent!.RigidTransform,
        ParentFrameKind.Attachment => ((BaseModelEntity)MoveParent!).ModelNode!.GetChildFrame(parentAttachmentName),
        _ => Matrix4x4.Identity,
    };

    // The frame the drawn transform is in this frame
    private Matrix4x4 GetParentRenderFrame() => parentFrameKind switch
    {
        ParentFrameKind.Entity => MoveParent!.renderFrame,
        ParentFrameKind.Attachment => GetParentFrame(),
        _ => Matrix4x4.Identity,
    };

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
        var isMoving = EntitySystem.Enabled && (MovedThisTick() || parentFrameKind == ParentFrameKind.Attachment);

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

        foreach (var node in placedNodes)
        {
            node.Transform = node.ApplyPlacementScale(Transform);
            Scene.DynamicOctree.Update(node);
        }
    }

    /// <summary>
    /// Puts a node this entity owns into the scene, and takes responsibility for its lifetime, whether it
    /// is drawn, and by default its placement. <see cref="RootNode"/> is the one the entity is drawn as; a
    /// model entity also owns the collision hulls its model was compiled with.
    /// </summary>
    /// <param name="node">The node to own.</param>
    /// <param name="followsEntity">
    /// Whether the entity places the node at itself. Pass <see langword="false"/> for a node placed some
    /// other way, such as an effect whose control point 0 belongs to another entity.
    /// </param>
    protected void AddNode(SceneNode node, bool followsEntity = true)
    {
        node.EntityData = Data;
        node.EntityInstance = this;

        // A node that came with a layer keeps it: the editor box is built on the editor-only layer so it
        // hides with the other markers, while geometry an entity really has belongs on the entity's own
        node.LayerName ??= LayerName;

        if (followsEntity)
        {
            node.Transform = Transform;
            placedNodes.Add(node);
        }

        // Only the hidden state is imposed, so a node that manages its own Visible keeps it while drawn
        if (!IsDrawn)
        {
            node.Visible = false;
        }

        ownedNodes.Add(node);
        Scene.Add(node, dynamic: true);
    }

    // The scene places these nodes after the parent model animates, so they never lag a frame behind it
    private void AttachToParentModel(SceneNodes.ModelSceneNode parentModel)
    {
        var local = Matrix4x4.CreateScale(EntityScale) * EntityTransformHelper.ToRigidTransformationMatrix(angles, origin);

        foreach (var node in placedNodes)
        {
            node.SetParent(parentModel, parentAttachmentName, node.ApplyPlacementScale(local));
        }

        placedNodes.Clear();
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
        placedNodes.Clear();
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
        SetTransform(origin, angles);
        UpdateColliderTransform();
    }

    /// <summary>
    /// Moves the collision shape onto the entity's current tick state.
    /// </summary>
    /// <remarks>
    /// Uses the tick state, not the interpolated one drawn this frame, because collision answers where
    /// the entity is - the same split the engine has between the server tracing and the client drawing.
    /// The transform stays rigid, leaving <see cref="EntityScale"/> out, because the shape's sweeps
    /// assume distances do not change in its local space.
    /// </remarks>
    protected void UpdateColliderTransform()
    {
        Collider?.Transform = RigidTransform;

        // Children ride this pose without moving themselves, so their shapes have to follow it here
        foreach (var child in moveChildren)
        {
            if (!child.IsRemoved)
            {
                child.UpdateColliderTransform();
            }
        }
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
        var drawnOrigin = Vector3.Lerp(previousOrigin, origin, fraction);
        var rotation = Quaternion.Slerp(
            EntityTransformHelper.EulerAnglesToQuaternion(previousAngles),
            EntityTransformHelper.EulerAnglesToQuaternion(angles),
            fraction);

        // A child interpolates in its parent frame, which the parent has interpolated already
        renderFrame = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(drawnOrigin) * GetParentRenderFrame();
        Transform = Matrix4x4.CreateScale(EntityScale) * renderFrame;

        transformDirty = true;
    }

    /// <summary>
    /// Drops the interpolation history, so the entity is drawn at its current state instead of sliding
    /// there from where it was. Source's <c>Interp_Reset</c>; call it after a teleport or any other jump
    /// that is not movement.
    /// </summary>
    protected void SnapInterpolation()
    {
        previousOrigin = origin;
        previousAngles = angles;
        isInterpolating = false;
        UpdateTransform();
    }

    private void SetTransform(Vector3 origin, Vector3 angles)
    {
        renderFrame = EntityTransformHelper.ToRigidTransformationMatrix(angles, origin) * GetParentRenderFrame();
        Transform = Matrix4x4.CreateScale(EntityScale) * renderFrame;

        transformDirty = true;
    }

    /// <summary>
    /// Wraps an angle into [0, 360), the way the engine's <c>anglemod</c> does, quantization included, so
    /// comparisons against a stored angle behave the same here as they do in Source.
    /// </summary>
    public static float AngleMod(float degrees)
        => 360f / 65536f * ((int)(degrees * (65536f / 360f)) & 65535);
}
