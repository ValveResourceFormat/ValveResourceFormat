using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>A physically simulated prop that can be pushed, carried and broken.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CPhysicsProp">CPhysicsProp</seealso>
public sealed class PropPhysics : BaseModelEntity, ICarryable, IDamageable
{
    [Flags]
    private enum PropSpawnFlags : uint
    {
        StartAsleep = 1,

        Debris = 4,

        MotionDisabled = 8,

        PreventPickup = 512,
    }

    /// <summary>Gets the rigid body, valid only while <see cref="HasBody"/>.</summary>
    public Body Body => body;

    // Struct setters mutate native state, and C# rejects them on a property copy
    private Body body;

    /// <summary>Gets whether a rigid body was built.</summary>
    public bool HasBody { get; private set; }

    /// <summary>Gets whether the player can pick this prop up.</summary>
    public bool CanBeCarried => HasBody && !HasSpawnFlags(PropSpawnFlags.PreventPickup) && !isPickupPrevented;

    private bool isPickupPrevented;

    /// <summary>Gets the player carrying this prop, or <see langword="null"/> when nobody is.</summary>
    public PlayerEntity? Carrier { get; private set; }

    /// <inheritdoc/>
    internal override bool TryGetConstraintBody(Vector3 anchor, out Body constraintBody)
    {
        constraintBody = body;
        return HasBody;
    }

    Body ICarryable.CarryBody => body;

    void ICarryable.BeginCarry(PlayerEntity carrier, float carryDistance, Body grabbedBody)
        => BeginCarry(carrier, carryDistance);

    (Vector3 Position, Quaternion? Rotation) ICarryable.ComputeHoldPose(float deltaTime) => ComputeHoldPose(deltaTime);

    /// <summary>Gets whether the player is carrying this prop right now.</summary>
    public bool IsCarried => Carrier != null;

    /// <summary>Gets how far ahead of the eyes the mass center is held while carried.</summary>
    public float CarryDistance { get; private set; }

    private Vector3 carryLocalMassCenter;
    private Quaternion carryRelativeRotation;

    private bool wasAwake;

    /// <summary>Gets the damage left before the prop breaks.</summary>
    public float Health { get; private set; }

    /// <summary>Gets whether a prop the player is holding can damage this one by striking it.</summary>
    public bool AcceptsDamageFromHeldObjects => KeyValues.GetBooleanProperty("acceptdamagefromheldobjects");

    /// <summary>Gets whether the model has health and the prop has not broken yet.</summary>
    public bool IsBreakable => breakData.IsBreakable && !isBroken;

    private PropBreakData breakData = Unbreakable;
    private bool isBroken;

    private ulong collisionCategory = PhysicsSimulation.PropCategory;

    // A negative group lets props dropped in quick succession pass through each other
    private const int ReleasedGroup = -1;
    private const float ReleaseClearance = 24f;
    private PlayerEntity? releasedFrom;
    private int collisionGroup;

    // Pieces take no impact damage, or low health glass shards shatter on landing
    private bool isBreakPiece;

    private static readonly PropBreakData Unbreakable = new(0f, 1f, 1f, 1f, false, [], [], false);

    /// <summary>Initializes a new instance of the <see cref="PropPhysics"/> class.</summary>
    public PropPhysics(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override bool UsesMoverBody => false;

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        // Out of movement traces, or a prop pinned against a wall can wedge the player
        IsSolid = false;

        // Not IsEmpty: it ignores sphere shapes, and only the rigid body uses them
        if (Collider is not { } collider)
        {
            return;
        }

        if (LoadedModel is { } model)
        {
            breakData = EntitySystem.PropData.Resolve(model);
            Health = breakData.Health;
        }

        var created = EntitySystem.Physics.CreatePropBody(
            collider.PhysicsData,
            collider.LocalBounds,
            Origin,
            EntityTransformHelper.EulerAnglesToQuaternion(Angles),
            motionEnabled: !HasSpawnFlags(PropSpawnFlags.MotionDisabled) && !breakData.SpawnMotionDisabled,
            startAsleep: HasSpawnFlags(PropSpawnFlags.StartAsleep),
            owner: this);

        if (created is { } newBody)
        {
            body = newBody;
            HasBody = true;
            wasAwake = newBody.IsAwake;

            if (HasSpawnFlags(PropSpawnFlags.Debris))
            {
                collisionCategory = PhysicsSimulation.DebrisCategory;
                SetCollidesWithPlayer(false);
            }
        }
    }

    /// <inheritdoc/>
    public void TakeDamage(in DamageInfo info)
    {
        if (!IsBreakable)
        {
            return;
        }

        var amount = info.Amount * breakData.ScaleFor(info.Type);

        if (amount <= 0f)
        {
            return;
        }

        Health -= amount;
        EntitySystem.TriggerOutput(this, "OnHealthChanged", info.Attacker);

        if (Health <= 0f)
        {
            Break(info.Attacker, info.Direction);
        }
    }

    /// <summary>Gets or sets the share of physics impact damage the prop takes; 1 takes all of it.</summary>
    public float ImpactDamageScale { get; set; } = 1f;

    internal void TakeImpact(float speed)
    {
        if (isBreakPiece)
        {
            return;
        }

        var damage = breakData.ImpactDamage(speed) * ImpactDamageScale;

        if (damage > 0f)
        {
            var velocity = HasBody ? body.LinearVelocity : Vector3.Zero;
            var direction = velocity.LengthSquared() > 1f ? Vector3.Normalize(velocity) : -Vector3.UnitZ;

            TakeDamage(new DamageInfo(damage, DamageType.Crush, Direction: direction));
        }
    }

    /// <summary>Fires <c>OnBreak</c>, spawns the break sound, effects and pieces, and removes the prop.</summary>
    public void Break(BaseEntity? attacker = null, Vector3 direction = default)
    {
        if (isBroken)
        {
            return;
        }

        isBroken = true;
        Health = 0f;

        EntitySystem.TriggerOutput(this, "OnBreak", attacker);

        if (HasBody)
        {
            BreakApart(this, breakData, body.Position, body.Rotation, body.LinearVelocity, body.AngularVelocity, body.CenterOfMass, direction);
        }
        else
        {
            BreakApart(this, breakData, Origin, EntityTransformHelper.EulerAnglesToQuaternion(Angles), Vector3.Zero, Vector3.Zero, Origin, direction);
        }

        EntitySystem.Remove(this);
    }

    // Well past any authored burst
    private const float BreakEffectLifetime = 10f;

    internal static void BreakApart(BaseModelEntity owner, PropBreakData breakData, Vector3 position, Quaternion rotation,
        Vector3 linearVelocity, Vector3 angularVelocity, Vector3 massCenter, Vector3 direction)
    {
        if (owner.Collider is { } collider && owner.EntitySystem.Physics.FindBreakSound(collider.PhysicsData) is { } sound)
        {
            Sound.Play(sound, massCenter);
        }

        foreach (var particle in breakData.Particles)
        {
            SpawnBreakEffect(owner, particle, position, rotation, linearVelocity, angularVelocity, massCenter, direction);
        }

        SpawnBreakPieces(owner, breakData, position, rotation, linearVelocity, angularVelocity, massCenter);
    }

    private static void SpawnBreakEffect(BaseModelEntity owner, BreakParticle particle, Vector3 position, Quaternion rotation,
        Vector3 linearVelocity, Vector3 angularVelocity, Vector3 massCenter, Vector3 direction)
    {
        var angles = EntityTransformHelper.ToEulerAngles(rotation);
        var data = new EntityLump.Entity { ParentLump = new EntityLump { Resource = new Resource() } };
        data.Add("classname", "info_particle_system");
        data.Add("effect_name", particle.Name);
        data.Add("origin", FormattableString.Invariant($"{position.X} {position.Y} {position.Z}"));
        data.Add("angles", FormattableString.Invariant($"{angles.X} {angles.Y} {angles.Z}"));

        if (particle.Snapshot != null)
        {
            data.Add("snapshot_file", particle.Snapshot);
        }

        if (owner.SpawnGroup is not { } spawnGroup
            || owner.EntitySystem.SpawnEntity(data, owner.LayerName, spawnGroup) is not InfoParticleSystem { Effect: { } effect } system)
        {
            return;
        }

        var skin = int.TryParse(owner.Data?.GetStringProperty("skin"), out var skinIndex) ? skinIndex : 0;
        var gravity = Vector3.Transform(new Vector3(0f, 0f, -PhysicsSimulation.GravityValue), Quaternion.Inverse(rotation));

        SetControlPoint(effect, particle.SkinControlPoint, new Vector3(skin, 0f, 0f));
        SetControlPoint(effect, particle.DamagePositionControlPoint, massCenter);
        SetControlPoint(effect, particle.DamageDirectionControlPoint, direction == Vector3.Zero ? -Vector3.UnitZ : direction);
        SetControlPoint(effect, particle.VelocityControlPoint, linearVelocity);
        SetControlPoint(effect, particle.AngularVelocityControlPoint, angularVelocity);
        SetControlPoint(effect, particle.LocalGravityControlPoint, gravity);

        owner.EntitySystem.QueueInput(system, "Kill", delay: BreakEffectLifetime);
    }

    private static void SetControlPoint(SceneNodes.ParticleSceneNode effect, int index, Vector3 value)
    {
        if (index >= 0)
        {
            effect.GetControlPoint(index).Position = value;
        }
    }

    private static void SpawnBreakPieces(BaseModelEntity owner, PropBreakData breakData, Vector3 position, Quaternion rotation,
        Vector3 linearVelocity, Vector3 angularVelocity, Vector3 massCenter)
    {
        var world = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        var skin = owner.Data?.GetStringProperty("skin");

        foreach (var piece in breakData.Pieces)
        {
            if (piece.SpawnChance < 1f && Random.Shared.NextSingle() >= piece.SpawnChance)
            {
                continue;
            }

            var pose = Matrix4x4.CreateFromQuaternion(EntityTransformHelper.EulerAnglesToQuaternion(piece.Angles))
                * Matrix4x4.CreateTranslation(piece.Offset)
                * world;
            var origin = pose.Translation;
            var angles = EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(pose));

            var data = new EntityLump.Entity { ParentLump = new EntityLump { Resource = new Resource() } };
            data.Add("classname", "prop_physics");
            data.Add("model", piece.Model);
            data.Add("origin", FormattableString.Invariant($"{origin.X} {origin.Y} {origin.Z}"));
            data.Add("angles", FormattableString.Invariant($"{angles.X} {angles.Y} {angles.Z}"));

            if (!string.IsNullOrEmpty(skin))
            {
                data.Add("skin", skin);
            }

            var created = owner.SpawnGroup is { } spawnGroup ? owner.EntitySystem.SpawnEntity(data, owner.LayerName, spawnGroup) : null;

            // Placeholder pieces with a null model
            if (created is not PropPhysics { HasBody: true } spawned)
            {
                if (created != null)
                {
                    owner.EntitySystem.Remove(created);
                }

                continue;
            }

            if (piece.Health > 0f)
            {
                spawned.OverrideHealth(piece.Health);
            }

            if (owner.ShownModelNode is { } shown && spawned.ModelNode is { } pieceNode)
            {
                pieceNode.TintAlpha = shown.TintAlpha;
            }

            if (piece.IsDebris)
            {
                spawned.collisionCategory = PhysicsSimulation.DebrisCategory;
            }

            if (piece.FadeTime > 0f)
            {
                spawned.RemoveAfter(piece.FadeTime);
            }

            if (FindPlacement(spawned) is { } placement)
            {
                var placed = placement * pose;
                spawned.Teleport(placed.Translation, EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(placed)));
            }

            spawned.isBreakPiece = true;
            spawned.isPickupPrevented = !piece.CanPlayerPickup;
            spawned.SetCollidesWithPlayer(!piece.IsDebris);

            var pieceBody = spawned.body;
            pieceBody.LinearVelocity = linearVelocity + Vector3.Cross(angularVelocity, pieceBody.CenterOfMass - massCenter);
            pieceBody.AngularVelocity = angularVelocity;
        }
    }

    // The placementOrigin attachment marks the whole model origin relative to the piece
    private static Matrix4x4? FindPlacement(PropPhysics piece)
    {
        if (piece.LoadedModel?.Attachments.GetValueOrDefault("placementOrigin") is not { Length: > 0 } attachment)
        {
            return null;
        }

        var influence = attachment[0];
        var rotation = attachment.IgnoreRotation ? Quaternion.Identity : influence.Rotation;
        var frame = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(influence.Offset);

        return Matrix4x4.Invert(frame, out var placement) ? placement : null;
    }

    private void OverrideHealth(float health)
    {
        breakData = breakData with { Health = health };
        Health = health;
    }

    private const float FadeOutDuration = 1f;

    private void RemoveAfter(float seconds)
    {
        fadeStartTime = EntitySystem.CurrentTime + seconds;
        SetNextThink(fadeStartTime);
    }

    private float fadeStartTime = -1f;
    private float? fadeFromAlpha;

    /// <inheritdoc/>
    public override void Think()
    {
        if (fadeStartTime < 0f)
        {
            return;
        }

        if (ModelNode is not { } node)
        {
            EntitySystem.Remove(this);
            return;
        }

        fadeFromAlpha ??= node.Alpha;

        var fade = (EntitySystem.CurrentTime - fadeStartTime) / FadeOutDuration;

        if (fade >= 1f)
        {
            EntitySystem.Remove(this);
            return;
        }

        node.Alpha = fadeFromAlpha.Value * (1f - fade);
        SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
    }

    [EntityInput("Break")]
    private void InputBreak(EntityInputData data) => Break(data.Activator);

    [EntityInput("RemoveHealth")]
    private void InputRemoveHealth(EntityInputData data) => TakeDamage(new DamageInfo(data.Float(), DamageType.Generic, data.Activator));

    [EntityInput("Wake")]
    private void InputWake(EntityInputData data)
    {
        if (HasBody)
        {
            body.IsAwake = true;
        }
    }

    [EntityInput("Sleep")]
    private void InputSleep(EntityInputData data)
    {
        if (HasBody && !IsCarried)
        {
            body.IsAwake = false;
        }
    }

    [EntityInput("EnableMotion")]
    private void InputEnableMotion(EntityInputData data)
    {
        if (HasBody && body.Type != BodyType.Dynamic)
        {
            body.Type = BodyType.Dynamic;
            body.IsAwake = true;
            EntitySystem.TriggerOutput(this, "OnMotionEnabled", data.Activator);
        }
    }

    [EntityInput("DisableMotion")]
    private void InputDisableMotion(EntityInputData data)
    {
        if (HasBody && !IsCarried)
        {
            body.Type = BodyType.Static;
        }
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        if (!HasBody || body.Type != BodyType.Dynamic)
        {
            return;
        }

        var isAwake = body.IsAwake;

        if (isAwake && !wasAwake)
        {
            EntitySystem.TriggerOutput(this, "OnAwakened");
        }

        wasAwake = isAwake;

        if (releasedFrom != null && !body.Bounds.Overlaps(releasedFrom.HullBounds.Inflate(ReleaseClearance)))
        {
            releasedFrom = null;
            collisionGroup = 0;
            SetCollidesWithPlayer(true);
        }

        SetOriginAndAngles(body.Position, EntityTransformHelper.ToEulerAngles(body.Rotation));
    }

    /// <inheritdoc/>
    public override void Teleport(Vector3 origin, Vector3? angles)
    {
        base.Teleport(origin, angles);

        if (HasBody)
        {
            body.SetTransform(Origin, EntityTransformHelper.EulerAnglesToQuaternion(Angles));
            body.LinearVelocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
        }
    }

    /// <summary>Lets the prop pass through the player and other released props until it is clear of them.</summary>
    public void ReleaseFrom(PlayerEntity player)
    {
        releasedFrom = player;
        collisionGroup = ReleasedGroup;
        SetCollidesWithPlayer(false);
    }

    /// <summary>Starts a carry, disabling gravity and player collision.</summary>
    public void BeginCarry(PlayerEntity carrier, float carryDistance)
    {
        // Grabbing enables motion for good
        if (body.Type != BodyType.Dynamic)
        {
            body.Type = BodyType.Dynamic;
            EntitySystem.TriggerOutput(this, "OnMotionEnabled", carrier);
        }

        EntitySystem.TriggerOutput(this, "OnPlayerPickup", carrier);

        Carrier = carrier;
        CarryDistance = carryDistance;
        releasedFrom = null;
        collisionGroup = 0;
        hasViewPoint = false;
        carryPress = 0f;
        carryLocalMassCenter = body.LocalCenterOfMass;
        carryRelativeRotation = Quaternion.Inverse(ViewRotation(carrier.GetHoldView().ViewAngles)) * body.Rotation;

        SetCollidesWithPlayer(false);

        body.GravityScale = 0f;
        body.CanSleep = false;
        body.IsAwake = true;
    }

    /// <summary>Ends the carry, restoring gravity and collision and keeping the velocity.</summary>
    public void EndCarry()
    {
        Carrier = null;
        SetCollidesWithPlayer(true);

        body.GravityScale = 1f;
        body.CanSleep = true;
        body.IsAwake = true;

        // Avoids a one frame hop from the live pose back to the interpolated one
        SnapInterpolation();
    }

    /// <inheritdoc/>
    protected override bool UpdatesRenderTransformEveryFrame => HasBody;

    /// <inheritdoc/>
    protected override void UpdateRenderTransform(float fraction)
    {
        // The world steps per frame, leaving an awake body nothing to interpolate
        if (HasBody && (IsCarried || body.IsAwake))
        {
            SetRenderTransform(body.Position, body.Rotation);
            return;
        }

        base.UpdateRenderTransform(fraction);
    }

    internal (Vector3 Position, Quaternion Rotation) ComputeHoldPose(float deltaTime)
    {
        var (eyePosition, forward, viewAngles) = Carrier!.GetHoldView();

        return ComputeHoldPose(eyePosition, forward, viewAngles, deltaTime);
    }

    // Pressing into a wall under a still view turns the prop flat; a pressed floor would tip it over
    private const float CarryPressDepth = 8f;
    private const float CarryStillSpeed = 50f;
    private const float MaxPressSurfaceNormalZ = 0.7f;

    private Vector3 previousViewPoint;
    private bool hasViewPoint;
    private float carryPress;

    private (Vector3 Position, Quaternion Rotation) ComputeHoldPose(Vector3 eyePosition, Vector3 forward, Vector3 viewAngles, float deltaTime)
    {
        var rotation = ViewRotation(viewAngles) * carryRelativeRotation;

        var reach = EntitySystem.Physics.TouchReach(eyePosition, forward, CarryDistance, body, out var surfaceNormal);
        var viewPoint = eyePosition + (forward * CarryDistance);

        if (deltaTime > 0f)
        {
            var isStill = hasViewPoint && Vector3.Distance(viewPoint, previousViewPoint) < CarryStillSpeed * deltaTime;
            var isWall = MathF.Abs(surfaceNormal.Z) < MaxPressSurfaceNormalZ && surfaceNormal != Vector3.Zero;
            carryPress = isWall && isStill ? CarryPressDepth : 0f;
            previousViewPoint = viewPoint;
            hasViewPoint = true;
        }

        reach = MathF.Min(reach + carryPress, CarryDistance);

        // Current rotation, not the target, or position and rotation fight while the turn catches up
        var position = eyePosition + forward * reach
            - Vector3.Transform(carryLocalMassCenter, body.Rotation);

        return (position, rotation);
    }

    // From angles, not the forward vector, whose yaw degenerates looking straight down
    internal static Quaternion ViewRotation(Vector3 viewAngles)
        => EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(viewAngles.X, viewAngles.Y, 0f));

    private void SetCollidesWithPlayer(bool collide)
    {
        var collidesWith = PhysicsSimulation.CollidesWith(collisionCategory);

        if (!collide)
        {
            collidesWith &= ~PhysicsSimulation.PlayerCategory;
        }

        Span<Shape> shapes = stackalloc Shape[body.ShapeCount];
        var count = body.GetShapes(shapes);

        for (var i = 0; i < count; i++)
        {
            // A constraint may have freed the prop of the world's static geometry
            var shapeCollidesWith = collidesWith & (shapes[i].Filter.CollidesWith | ~PhysicsSimulation.StaticCategory);
            shapes[i].SetFilter(new CollisionFilter(collisionCategory, shapeCollidesWith, collisionGroup), recomputeContacts: true);
        }
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

        if (HasBody)
        {
            EntitySystem.PhysicsOrNull?.Forget(Body);
            body.Destroy();
            HasBody = false;
        }
    }
}
