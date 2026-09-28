using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A physically simulated prop, Source's <c>CPhysicsProp</c>: it falls, tumbles, gets shoved by the
/// player, and can be picked up and carried with +USE the way Half-Life 2 does it. The model and its
/// collision shape come from <see cref="BaseModelEntity"/>; the movement comes from a dynamic body in
/// <see cref="PhysicsSimulation"/> whose pose the entity adopts every tick.
/// </summary>
public sealed class PropPhysics : BaseModelEntity, ICarryable, IDamageable
{
    /// <summary>The <c>spawnflags</c> a physics prop reads, Source's <c>SF_PHYSPROP_*</c>.</summary>
    [Flags]
    private enum PropSpawnFlags : uint
    {
        /// <summary>The body waits for a touch before it starts simulating.</summary>
        StartAsleep = 1,

        /// <summary>
        /// The prop does not simulate until something enables its motion; being grabbed counts,
        /// the way the gravity gloves free a pinned prop. Until then it stands as a static
        /// obstacle other props collide with.
        /// </summary>
        MotionDisabled = 8,

        /// <summary>The player can never pick this prop up, as authored.</summary>
        PreventPickup = 512,
    }

    /// <summary>Gets the rigid body simulating this prop. Only meaningful while <see cref="HasBody"/>.</summary>
    public Body Body => body;

    // The struct's setters mutate native state, which C# will not allow through a property copy
    private Body body;

    /// <summary>Gets whether a rigid body was built; a prop whose model carries no collision has none.</summary>
    public bool HasBody { get; private set; }

    /// <summary>
    /// Gets whether the player can pick this prop up: it has a body, and the map did not forbid
    /// it. A motion-disabled prop counts - grabbing it is what enables its motion, the way the
    /// gravity gloves free a prop pinned in place.
    /// </summary>
    public bool CanBeCarried => HasBody && !HasSpawnFlags(PropSpawnFlags.PreventPickup);

    /// <summary>Gets the player carrying this prop, or <see langword="null"/> when nobody is.</summary>
    public PlayerEntity? Carrier { get; private set; }

    /// <inheritdoc/>
    internal override bool TryGetConstraintBody(Vector3 anchor, out Body constraintBody)
    {
        constraintBody = body;
        return HasBody;
    }

    // The carry steers the whole prop: its one body, orientation and all
    Body ICarryable.CarryBody => body;

    void ICarryable.BeginCarry(PlayerEntity carrier, float carryDistance, Body grabbedBody)
        => BeginCarry(carrier, carryDistance);

    (Vector3 Position, Quaternion? Rotation) ICarryable.ComputeHoldPose() => ComputeHoldPose();

    void ICarryable.AdoptCarryRotation(float fraction) => AdoptCarryRotation(fraction);

    /// <summary>Gets whether the player is carrying this prop right now.</summary>
    public bool IsCarried => Carrier != null;

    /// <summary>Gets how far ahead of the eyes the mass center is held while carried.</summary>
    public float CarryDistance { get; private set; }

    // The grab in the body's frame: where the mass center sits (the hold point steers the mass
    // center, not the body origin, so the prop hangs centered under the crosshair) and how the body
    // was oriented relative to the view when grabbed
    private Vector3 carryLocalMassCenter;
    private Quaternion carryRelativeRotation;

    // The body's sleep state as of the last tick, for the OnAwakened edge
    private bool wasAwake;

    /// <summary>Gets the damage left before the prop breaks.</summary>
    public float Health { get; private set; }

    /// <summary>Gets whether the prop can still be broken: its model has health, and it has not broken yet.</summary>
    public bool IsBreakable => breakData.IsBreakable && !isBroken;

    private PropBreakData breakData = Unbreakable;
    private bool isBroken;

    // Debris - the pieces of something broken - stays out of the player's way and out of other debris
    private ulong collisionCategory = PhysicsSimulation.PropCategory;

    // The pieces of one break share a negative collision group, which never collides with itself:
    // pieces are cut to fit together, their hulls overlap where they meet, and colliding siblings
    // would be shoved apart at spawn as if blown up
    private int collisionGroup;
    private static int lastBreakGroup;

    // A piece takes no impact damage: shards with a sliver of health and a glass impact table would
    // shatter the moment they landed, and the whole prop would seem to vanish at the first hit
    private bool isBreakPiece;

    private static readonly PropBreakData Unbreakable = new(0f, 1f, 1f, 1f, false, [], []);

    /// <summary>
    /// Initializes the prop from its keyvalues.
    /// </summary>
    public PropPhysics(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override bool UsesMoverBody => false;

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        // Soft collision only: the player shoves props through the kinematic pushing body in the
        // rigid body world, and props never enter the movement traces. A hard-blocking prop pinned
        // against a wall is a prop the player gets stuck on; a soft one just gets pushed or passed
        // through, which can never wedge the player.
        IsSolid = false;

        // The collider was built alongside the model; a prop compiled without physics stays where
        // the map put it, exactly like an unimplemented classname would. IsEmpty is deliberately
        // not checked: it only speaks for the traced hulls and meshes, and a sphere-collision prop
        // (a soccer ball, say) is "empty" to the tracer while being exactly what belongs here.
        if (Collider is not { } collider)
        {
            return;
        }

        var created = EntitySystem.Physics.CreatePropBody(
            collider.PhysicsData,
            collider.LocalBounds,
            Origin,
            EntityTransformHelper.EulerAnglesToQuaternion(Angles),
            motionEnabled: !HasSpawnFlags(PropSpawnFlags.MotionDisabled),
            startAsleep: HasSpawnFlags(PropSpawnFlags.StartAsleep),
            owner: this);

        if (created is { } newBody)
        {
            body = newBody;
            HasBody = true;
            wasAwake = newBody.IsAwake;
        }

        if (LoadedModel is { } model)
        {
            breakData = EntitySystem.PropData.Resolve(model);
            Health = breakData.Health;
        }

        // Only a prop that can break needs to hear how hard it was struck
        if (HasBody && IsBreakable)
        {
            SetHitEvents(true);
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

    /// <summary>
    /// Takes the damage of a physics impact at <paramref name="speed"/>, from the prop's impact table.
    /// </summary>
    internal void TakeImpact(float speed)
    {
        if (isBreakPiece)
        {
            return;
        }

        var damage = breakData.ImpactDamage(speed);

        if (damage > 0f)
        {
            var velocity = HasBody ? body.LinearVelocity : Vector3.Zero;
            var direction = velocity.LengthSquared() > 1f ? Vector3.Normalize(velocity) : -Vector3.UnitZ;

            TakeDamage(new DamageInfo(damage, DamageType.Crush, Direction: direction));
        }
    }

    /// <summary>
    /// Breaks the prop: <c>OnBreak</c> fires, it plays its break sound and effects, the pieces its
    /// model lists spawn where their parts of it were, moving as the prop moved, and it is removed.
    /// </summary>
    /// <param name="attacker">Who broke it, for <c>OnBreak</c>.</param>
    /// <param name="direction">Which way the breaking hit travelled, for the effects; zero when unknown.</param>
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

    // How long a break effect is kept before it is removed, well past any authored burst
    private const float BreakEffectLifetime = 10f;

    /// <summary>
    /// What happens where a model breaks, for any prop that breaks: its surface's break sound, its
    /// break effects, and its pieces.
    /// </summary>
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

    // The effect lives on a particle entity of its own, which outlasts the broken prop and is
    // killed once the effect has long finished
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

        if (owner.EntitySystem.CreateEntity(data, Matrix4x4.Identity, owner.LayerName, owner.Scene) is not InfoParticleSystem { Effect: { } effect } system)
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

    /// <summary>
    /// Spawns the pieces a broken model lists, each where its part of the model was, carrying on with
    /// the motion that part had, spin included.
    /// </summary>
    private static void SpawnBreakPieces(BaseModelEntity owner, PropBreakData breakData, Vector3 position, Quaternion rotation,
        Vector3 linearVelocity, Vector3 angularVelocity, Vector3 massCenter)
    {
        var world = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        var skin = owner.Data?.GetStringProperty("skin");
        var group = --lastBreakGroup;

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

            var created = owner.EntitySystem.CreateEntity(data, Matrix4x4.Identity, owner.LayerName, owner.Scene);

            // A placeholder piece - a door's null model - has nothing to simulate or draw
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

            if (piece.IsDebris)
            {
                spawned.collisionCategory = PhysicsSimulation.DebrisCategory;
            }

            if (piece.FadeTime > 0f)
            {
                spawned.RemoveAfter(piece.FadeTime);
            }

            spawned.isBreakPiece = true;
            spawned.collisionGroup = group;
            spawned.SetCollidesWithPlayer(!piece.IsDebris);

            // No push of its own: a piece goes where its part of the prop was going, and only a
            // burst the model authors would scatter it
            var pieceBody = spawned.body;
            pieceBody.LinearVelocity = linearVelocity + Vector3.Cross(angularVelocity, pieceBody.CenterOfMass - massCenter);
            pieceBody.AngularVelocity = angularVelocity;
        }
    }

    private void OverrideHealth(float health)
    {
        breakData = breakData with { Health = health };
        Health = health;
        SetHitEvents(true);
    }

    private void RemoveAfter(float seconds)
    {
        removeTime = EntitySystem.CurrentTime + seconds;
        SetNextThink(removeTime);
    }

    private float removeTime = -1f;

    /// <inheritdoc/>
    public override void Think()
    {
        if (removeTime >= 0f && EntitySystem.CurrentTime >= removeTime && !IsCarried)
        {
            EntitySystem.Remove(this);
        }
    }

    private void SetHitEvents(bool enabled)
    {
        Span<Shape> shapes = stackalloc Shape[body.ShapeCount];
        var count = body.GetShapes(shapes);

        for (var i = 0; i < count; i++)
        {
            shapes[i].HitEventsEnabled = enabled;
        }
    }

    [EntityInput("Break")]
    private void InputBreak(EntityInputData data) => Break(data.Activator);

    [EntityInput("RemoveHealth")]
    private void InputRemoveHealth(EntityInputData data) => TakeDamage(new DamageInfo(data.Float(), DamageType.Generic, data.Activator));

    // A body put to sleep stays where it is even when what held it up is gone, so maps wake the
    // lid of a pot when the pot breaks
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

        // A sleeping prop coming to life is an authored event, Source's OnAwakened; the map may
        // have wired an alarm to the can the player knocked over
        var isAwake = body.IsAwake;

        if (isAwake && !wasAwake)
        {
            EntitySystem.TriggerOutput(this, "OnAwakened");
        }

        wasAwake = isAwake;

        // Adopt the pose the frame-stepped world has moved the body to, so the entity state and
        // its collider follow the prop wherever it tumbles. The drawing does not wait for this:
        // an awake body is drawn at its live pose every frame.
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

    /// <summary>
    /// Puts the prop in the player's hands: gravity lets go, the pushing body stops colliding with
    /// it, and the carry logic in <see cref="PlayerEntity"/> starts steering the body.
    /// </summary>
    /// <param name="carrier">The player doing the carrying.</param>
    /// <param name="carryDistance">How far ahead of the eyes the mass center is held.</param>
    public void BeginCarry(PlayerEntity carrier, float carryDistance)
    {
        // Grabbing a motion-disabled prop enables its motion, permanently: once freed it stays a
        // simulating body, which is what the engine's EnableMotion on grab amounts to
        if (body.Type != BodyType.Dynamic)
        {
            body.Type = BodyType.Dynamic;
            EntitySystem.TriggerOutput(this, "OnMotionEnabled", carrier);
        }

        EntitySystem.TriggerOutput(this, "OnPlayerPickup", carrier);

        Carrier = carrier;
        CarryDistance = carryDistance;
        carryLocalMassCenter = body.LocalCenterOfMass;
        carryRelativeRotation = Quaternion.Inverse(ViewRotation(carrier.GetHoldView().ViewAngles)) * body.Rotation;

        // Off the pushing body, so the held prop cannot wedge against its carrier
        SetCollidesWithPlayer(false);

        body.GravityScale = 0f;
        body.CanSleep = false;
        body.IsAwake = true;

    }

    /// <summary>
    /// Lets go of the prop, restoring gravity and collision. The body keeps whatever velocity the
    /// carry left it with, which is how a walking or turning player lends the prop their motion.
    /// </summary>
    public void EndCarry()
    {
        Carrier = null;
        SetCollidesWithPlayer(true);

        body.GravityScale = 1f;
        body.CanSleep = true;
        body.IsAwake = true;

        // Back to plain interpolation next frame; snapping the history trims the one-frame hop
        // from the camera-glued pose to the tick-lagged one when dropped mid-stride
        SnapInterpolation();
    }

    /// <summary>
    /// Relaxes the grip part way toward the body's current orientation, for when the world is
    /// twisting the held prop away from the hold rotation: the twist gradually becomes the
    /// carried orientation instead of an error the carry keeps fighting. Only the chase target
    /// moves - the drawing reads the body itself - so nothing visible jumps.
    /// </summary>
    /// <param name="fraction">How much of the way to the body's orientation the grip moves.</param>
    internal void AdoptCarryRotation(float fraction)
    {
        var view = ViewRotation(Carrier!.GetHoldView().ViewAngles);

        carryRelativeRotation = Quaternion.Slerp(carryRelativeRotation,
            Quaternion.Inverse(view) * body.Rotation, fraction);
    }

    /// <inheritdoc/>
    protected override bool UpdatesRenderTransformEveryFrame => HasBody;

    /// <summary>
    /// Draws an awake body at its live pose. The world steps with the rendered frame, so the
    /// body's pose IS this frame's pose - there is nothing to interpolate and no tick rate to
    /// see. A sleeping body has not moved since its tick state, which the base interpolation
    /// draws exactly.
    /// </summary>
    protected override void UpdateRenderTransform(float fraction)
    {
        if (HasBody && (IsCarried || body.IsAwake))
        {
            SetRenderTransform(body.Position, body.Rotation);
            return;
        }

        base.UpdateRenderTransform(fraction);
    }

    /// <summary>
    /// Where the carried body belongs, by the carrier's hold view.
    /// </summary>
    internal (Vector3 Position, Quaternion Rotation) ComputeHoldPose()
    {
        var (eyePosition, forward, viewAngles) = Carrier!.GetHoldView();

        return ComputeHoldPose(eyePosition, forward, viewAngles);
    }

    /// <summary>
    /// Where the carried body belongs for a given view: the mass center on the eye ray at the
    /// carry distance, the grab orientation turned with the view. There is no attach glide - the
    /// carry's bounded acceleration is what pulls a distant grab over smoothly - and the pose is
    /// not clamped against the world: aiming into a wall asks for a pose inside it, the solver's
    /// contacts hold the body at the surface, and a prop held far from an unreachable pose for
    /// long enough is let go by the strain drop.
    /// </summary>
    private (Vector3 Position, Quaternion Rotation) ComputeHoldPose(Vector3 eyePosition, Vector3 forward, Vector3 viewAngles)
    {
        var rotation = ViewRotation(viewAngles) * carryRelativeRotation;

        // Offsetting by the rotated local mass center is what puts the *center* of the prop under
        // the crosshair, wherever its body origin happens to sit. The body's own rotation, not the
        // target's: while the turn is still catching up, aiming the origin with the target
        // rotation would push the actual mass center off the ray and set position and rotation
        // fighting each other.
        var position = eyePosition + forward * CarryDistance
            - Vector3.Transform(carryLocalMassCenter, body.Rotation);

        return (position, rotation);
    }

    /// <summary>
    /// The view as a rotation, pitch and yaw only, so a carried prop turns with the whole view
    /// the way the gravgun's held objects do. Built from the view angles rather than the forward
    /// vector: a reconstructed yaw degenerates looking straight down, and the whipping target
    /// rotation used to spin the carried prop there.
    /// </summary>
    internal static Quaternion ViewRotation(Vector3 viewAngles)
        => EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(viewAngles.X, viewAngles.Y, 0f));

    private void SetCollidesWithPlayer(bool collide)
    {
        // Debris never meets the pushing hull, which collides with props alone, nor other debris
        var collidesWith = collisionCategory == PhysicsSimulation.DebrisCategory
            ? ulong.MaxValue & ~PhysicsSimulation.DebrisCategory
            : collide ? ulong.MaxValue : ulong.MaxValue & ~PhysicsSimulation.PlayerCategory;

        Span<Shape> shapes = stackalloc Shape[body.ShapeCount];
        var count = body.GetShapes(shapes);

        for (var i = 0; i < count; i++)
        {
            shapes[i].SetFilter(new CollisionFilter(collisionCategory, collidesWith, collisionGroup), recomputeContacts: true);
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
