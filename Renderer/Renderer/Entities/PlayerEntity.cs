using Box3D;
using ValveResourceFormat.Renderer.Input;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The player as an entity, mirroring an <see cref="IPlayerController"/> so triggers can touch it
/// and teleports can move it.
/// </summary>
/// <remarks>
/// The controller owns the position and moves per rendered frame, not on the entity tick, so
/// <see cref="TryGetTouchBounds"/> reads the hull live rather than the last tick's copy.
/// </remarks>
public sealed class PlayerEntity : BaseEntity
{
    /// <summary>How far the player can reach to press something.</summary>
    public float UseRange { get; set; } = 80f;

    // The +USE carry: how far the pickup trace reaches, and how far ahead of the mass center the
    // base hold point sits
    private const float PickupReach = 300f;
    private const float HoldDistance = 40f;

    // The shadow controller: the held body hangs off a kinematic hand, moved onto the hold pose
    // every frame, by the damped springs of a motor joint. Their force and torque are capped, so
    // the body follows the view closely in the open while anything solid in the way holds its
    // ground: a wall stops it with a steady push rather than being rammed, and a contact off the
    // mass center turns it, which is how a plank pushed into a wall turns to fit. The caps are
    // accelerations, so a heavy prop is held as firmly as a light one.
    private const float CarryHertz = 12f;
    private const float CarryDampingRatio = 1f;
    private const float MaxCarryAcceleration = 20000f;
    private const float MaxCarryAngularAcceleration = 150f;

    // A view flick throws the held body no faster than this
    private const float MaxCarrySpeed = 1000f;

    // A fresh pickup's hand starts at the body and travels to the hold pose no faster than this,
    // braking to arrive, so a prop pulled in from afar comes steadily rather than flung at the
    // full spring
    private const float PickupTravelSpeed = 500f;
    private const float PickupTravelBraking = 4000f;

    // A held prop takes part of its impact damage, so pressing it into the world as it turns to fit
    // does not shatter everything fragile
    private const float CarriedImpactDamageScale = 0.4f;

    // How long the prop may strain far from the hold pose before the player loses hold of it
    private const float CarryBreakDistance = 64f;
    private const float CarryBreakTime = 0.5f;

    /// <summary>Gets the controller whose state this entity reflects.</summary>
    public IPlayerController Controller { get; }

    internal BoundingBox HullBounds => BoundingBox.FromCenter(Origin + new Vector3(0f, 0f, Controller.HullHalfExtents.Z), Controller.HullHalfExtents);

    /// <summary>Gets what the player is carrying, or <see langword="null"/> when their hands are free.</summary>
    public ICarryable? Carried { get; private set; }

    // Held and changed buttons, latched once per tick
    private PlayerButtonState Buttons;

    // The kinematic body standing where the player stands, so walking into props shoves them
    private Body presenceBody;
    private bool hasPresenceBody;

    // The debris shove, see ShoveDebris
    private const float MinShoveSpeed = 20f;
    private const float MaxShoveSpeed = 350f;
    private const float ShoveMargin = 2f;

    // What pushes the player this tick, summed over every trigger_push touching them
    private Vector3 baseVelocity;

    /// <summary>Adds to what pushes the player on the next tick, such as a <c>trigger_push</c>.</summary>
    /// <param name="velocity">The push velocity.</param>
    public void AddBaseVelocity(Vector3 velocity) => baseVelocity += velocity;

    // Reused by the debris shove every tick
    private readonly List<Body> debrisBodies = [];
    private readonly HashSet<ulong> debrisBodyIds = [];

    // How long the carried prop has been stuck far from its hold pose; a flick spikes this for a
    // tick or two, a wedged prop keeps it climbing until the carry gives up
    private float carryStrainTime;

    // The kinematic hand the carried body hangs off, and the joint it hangs by
    private Body carryHand;
    private MotorJoint carryJoint;
    private bool hasCarryJoint;

    // Whether the hand has reached the hold pose since the pickup
    private bool carryHandArrived;

    /// <summary>Creates the player entity for a movement controller.</summary>
    public PlayerEntity(EntitySystem system, Scene scene, IPlayerController controller) : base(system, scene, "player")
    {
        Controller = controller;

        // Traces ignore the player, which enters triggers rather than being one
        IsSolid = false;
    }

    /// <inheritdoc/>
    public override bool TryGetTouchBounds(out Vector3 center, out Vector3 halfExtents)
    {
        halfExtents = Controller.HullHalfExtents;
        center = Controller.Position + new Vector3(0, 0, halfExtents.Z);
        return true;
    }

    /// <summary>
    /// Teleports the player. <see cref="BaseEntity.WorldOrigin"/> is the feet, as in
    /// <see cref="IPlayerController.Teleport"/>, so the destination is passed through unchanged.
    /// </summary>
    public override void Teleport(Vector3 origin, Vector3? angles)
    {
        Controller.Teleport(origin, angles);
        SyncFromController();
    }

    /// <summary>
    /// Presses whatever the player is looking at within <see cref="UseRange"/>, the <c>+use</c> command.
    /// </summary>
    /// <returns>The entity that was pressed, or <see langword="null"/> if nothing was in reach.</returns>
    public BaseEntity? PressUse()
    {
        var from = Controller.EyePosition;
        var target = EntitySystem.FindUseTarget(from, from + Controller.ViewForward * UseRange);

        target?.Use(this);

        return target;
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        // Resets key latches inside player movement, if a key is presseed,
        // unpressed and pressed again in within 3 frames the tick will see it as one press.
        Buttons = Controller.ConsumeButtons();

        // The pushes touched last tick, or none, which hands the last push over as momentum
        Controller.SetBaseVelocity(baseVelocity);
        baseVelocity = Vector3.Zero;

        // The controller owns the position, nothing to integrate
        SyncFromController();

        if (Buttons.Pressed(TrackedKeys.E))
        {
            // The engine's +use order: hands full means let go, a usable entity in reach wins the
            // press, and only a press nothing answered becomes a pickup attempt
            if (Carried != null)
            {
                Drop();
            }
            else if (PressUse() == null)
            {
                TryPickup();
            }
        }

        UpdatePhysicsPresence(tickInterval);
    }

    /// <inheritdoc/>
    internal override void FrameSimulate(float frameTime)
    {
        // Per frame, not per tick: the carry chases the camera, and a chase sampled at 64 Hz
        // reads back quantized however smoothly it is drawn
        UpdateCarry(frameTime);
    }

    private void SyncFromController()
    {
        WorldOrigin = Controller.Position;
        Velocity = Controller.Velocity;
    }

    /// <summary>
    /// Keeps the kinematic pushing body where the player stands. Moving it with a velocity rather
    /// than teleporting it is what lets the solver shove props out of the way with the player's
    /// real speed; a jump across the map is not a shove, so that snaps instead.
    /// </summary>
    private void UpdatePhysicsPresence(float tickInterval)
    {
        // Only once something else has built the rigid body world; a player in a map with no
        // physics props has nothing to push
        if (EntitySystem.PhysicsOrNull is not { } physics)
        {
            return;
        }

        if (!hasPresenceBody)
        {
            presenceBody = physics.CreatePlayerBody(Origin, Controller.HullHalfExtents);
            hasPresenceBody = true;
            return;
        }

        if (Vector3.DistanceSquared(presenceBody.Position, Origin) > 256f * 256f)
        {
            presenceBody.SetTransform(Origin, null);
        }
        else
        {
            presenceBody.MoveTowards(Origin, Quaternion.Identity, tickInterval, wake: true);
        }

        ShoveDebris(physics);
    }

    /// <summary>
    /// Shoves the debris and ragdoll parts the player's hull overlaps with a purely horizontal
    /// velocity, away from the player at the player's own speed. Neither meets the pushing body in
    /// the solver (see <see cref="PhysicsSimulation.DebrisCategory"/> and
    /// <see cref="PhysicsSimulation.RagdollCategory"/>), and a horizontal velocity can never press
    /// anything into the ground.
    /// </summary>
    private void ShoveDebris(PhysicsSimulation physics)
    {
        var horizontalVelocity = Velocity with { Z = 0f };
        var speed = horizontalVelocity.Length();

        // A still player is not a jitter source for whatever they stand in
        if (speed < MinShoveSpeed)
        {
            return;
        }

        var halfExtents = Controller.HullHalfExtents + new Vector3(ShoveMargin, ShoveMargin, 0f);
        var center = Origin + new Vector3(0f, 0f, Controller.HullHalfExtents.Z);

        // Collected first, shoved after: the world is locked while a query runs
        debrisBodies.Clear();
        debrisBodyIds.Clear();

        var overlapped = new DebrisOverlap(debrisBodies, debrisBodyIds);
        physics.World.OverlapBox(center, halfExtents, ref overlapped,
            new QueryFilter(~PhysicsSimulation.PlayerCategory, PhysicsSimulation.DebrisCategory | PhysicsSimulation.RagdollCategory));

        var heldBody = Carried?.CarryBody;
        var pushSpeed = MathF.Min(speed, MaxShoveSpeed);

        foreach (var body in debrisBodies)
        {
            // The carried body is the carry's to steer, not the hull's to shove
            if (body.UserData == heldBody?.UserData)
            {
                continue;
            }

            var direction = (body.Position - center) with { Z = 0f };
            var distance = direction.Length();

            // Radially away from the player, or along the motion when standing dead centered
            direction = distance > 0.5f ? direction / distance : horizontalVelocity / speed;

            var current = Vector3.Dot(body.LinearVelocity, direction);

            if (current < pushSpeed)
            {
                var pushed = body;
                pushed.LinearVelocity += direction * (pushSpeed - current);
                pushed.IsAwake = true;
            }
        }
    }

    // One entry per body however many of its shapes overlap
    private readonly struct DebrisOverlap(List<Body> bodies, HashSet<ulong> bodyIds) : IOverlapCallback
    {
        public bool OnOverlap(Shape shape)
        {
            var body = shape.Body;

            if (bodyIds.Add(body.UserData))
            {
                bodies.Add(body);
            }

            return true;
        }
    }

    /// <summary>
    /// The +USE carry, the way Half-Life 2's shadow controller holds what the player picks up: the
    /// hand moves onto the hold pose, and the joint pulls the body after it. The body keeps its
    /// velocity on release, so dropping mid-stride carries the player's motion and a view flick is
    /// a throw.
    /// </summary>
    private void UpdateCarry(float deltaTime)
    {
        // A zero-length frame has no step to move the hand over
        if (deltaTime <= 1e-6f || Carried is not { } carried || !hasCarryJoint)
        {
            return;
        }

        if (carried.IsRemoved)
        {
            // Gone from the world mid-carry; nothing left to restore state on
            ReleaseCarryJoint();
            Carried = null;
            return;
        }

        var body = carried.CarryBody;
        var (handPosition, handRotation) = ComputeHandPose(carried, body, deltaTime);

        if (!carryHandArrived)
        {
            var travel = handPosition - carryHand.Position;
            var distance = travel.Length();
            var maxTravel = MathF.Min(PickupTravelSpeed, MathF.Sqrt(2f * PickupTravelBraking * distance)) * deltaTime;

            if (distance > maxTravel)
            {
                handPosition = carryHand.Position + (Vector3.Normalize(travel) * maxTravel);
            }
            else
            {
                carryHandArrived = true;
            }
        }

        carryHand.MoveTowards(handPosition, handRotation, deltaTime, wake: true);

        // A prop that stays far from its hold pose is wedged somewhere it cannot leave; the
        // engine drops what it cannot keep hold of, but only after a strain the flick of a
        // throw never sustains
        if (Vector3.Distance(body.CenterOfMass, handPosition) > CarryBreakDistance)
        {
            carryStrainTime += deltaTime;

            if (carryStrainTime >= CarryBreakTime)
            {
                Drop();
                return;
            }
        }
        else
        {
            carryStrainTime = 0f;
        }

        var velocity = body.LinearVelocity;
        var speed = velocity.Length();

        if (speed > MaxCarrySpeed)
        {
            body.LinearVelocity = velocity * (MaxCarrySpeed / speed);
        }
    }

    // Where the hand goes: the held body's mass center at the hold pose, turned as the hold pose
    // is, or as the body is when only its position is held
    private static (Vector3 Position, Quaternion Rotation) ComputeHandPose(ICarryable carried, Body body, float deltaTime)
    {
        var (holdPosition, holdRotation) = carried.ComputeHoldPose(deltaTime);
        var rotation = holdRotation ?? body.Rotation;

        return (holdPosition + Vector3.Transform(body.LocalCenterOfMass, rotation), rotation);
    }

    private void CreateCarryJoint(PhysicsSimulation physics, ICarryable carried, Body body)
    {
        var (_, handRotation) = ComputeHandPose(carried, body, deltaTime: 0f);
        var holdsRotation = carried.ComputeHoldPose(deltaTime: 0f).Rotation is not null;
        carryHand = physics.World.CreateKinematicBody(body.CenterOfMass, handRotation);
        carryHandArrived = false;

        // The turning caps as a torque need the body's rotational inertia, which the solver keeps
        // to itself; its mass at the reach of its shapes stands in
        Span<Shape> shapes = stackalloc Shape[body.ShapeCount];
        var count = body.GetShapes(shapes);
        var radius = 0f;

        for (var i = 0; i < count; i++)
        {
            var bounds = shapes[i].Bounds;
            radius = MathF.Max(radius, Vector3.Distance(body.CenterOfMass, bounds.Center) + bounds.Extents.Length());
        }

        carryJoint = physics.World.CreateMotorJoint(MotorJointDefinition.Default with
        {
            Base = JointDefinition.Connect(carryHand, body, JointFrame.Identity, new JointFrame(body.LocalCenterOfMass)),
            LinearHertz = CarryHertz,
            LinearDampingRatio = CarryDampingRatio,
            MaxSpringForce = body.Mass * MaxCarryAcceleration,
            AngularHertz = holdsRotation ? CarryHertz : 0f,
            AngularDampingRatio = CarryDampingRatio,
            MaxSpringTorque = holdsRotation ? body.Mass * radius * radius * MaxCarryAngularAcceleration : 0f,
            MaxVelocityForce = 0f,
            MaxVelocityTorque = 0f,
        });
        hasCarryJoint = true;
    }

    private void ReleaseCarryJoint()
    {
        if (!hasCarryJoint)
        {
            return;
        }

        // Gone already when the carried body was destroyed, by breaking
        if (carryJoint.AsJoint.IsValid)
        {
            carryJoint.AsJoint.Destroy();
        }

        carryHand.Destroy();
        hasCarryJoint = false;
    }

    /// <summary>
    /// The view a carried object is held in front of: the camera the frame is drawn with when
    /// there is one, since view smoothing and punch sit between the input camera and the drawn
    /// view, and a carry steered by the wrong one trails the picture by exactly that gap.
    /// </summary>
    internal (Vector3 EyePosition, Vector3 Forward, Vector3 ViewAngles) GetHoldView()
    {
        if (EntitySystem.RenderCamera is { } camera)
        {
            return (camera.Location, camera.Forward, camera.GetQAngle());
        }

        return (Controller.EyePosition, Controller.ViewForward, Controller.ViewAngles);
    }

    private void TryPickup()
    {
        if (EntitySystem.PhysicsOrNull is not { } physics)
        {
            return;
        }

        var eyePosition = Controller.EyePosition;
        var forward = Controller.ViewForward;

        // Cast against the world and the movers too, not just props, so a crate behind a wall or
        // a closed door is not grabbable through it: the obstacle wins the raycast and the pickup
        // finds nothing. Not as the player, whom debris is filtered against.
        var hit = physics.World.RaycastClosest(eyePosition, forward * PickupReach,
            new QueryFilter(~PhysicsSimulation.PlayerCategory,
                PhysicsSimulation.StaticCategory | PhysicsSimulation.PropCategory
                | PhysicsSimulation.DebrisCategory | PhysicsSimulation.RagdollCategory | PhysicsSimulation.MoverCategory));

        if (!hit.Hit)
        {
            return;
        }

        var grabbedBody = hit.Shape.Body;

        if (physics.GetOwner(grabbedBody) is not ICarryable carryable || carryable.IsRemoved || !carryable.CanBeCarried)
        {
            return;
        }

        // The sandbox gravgun's hold distance: a fixed reach plus how far the grabbed surface sits
        // from the mass center, so a big crate hangs further out than a soda can and neither clips
        // the player's hull. The grabbed body's own center, which for a ragdoll is the one part
        // under the crosshair rather than the whole body.
        Carried = carryable;
        carryStrainTime = 0f;
        carryable.BeginCarry(this, HoldDistance + Vector3.Distance(hit.Point, grabbedBody.CenterOfMass), grabbedBody);
        CreateCarryJoint(physics, carryable, carryable.CarryBody);

        if (carryable is PropPhysics prop)
        {
            prop.ImpactDamageScale *= CarriedImpactDamageScale;
        }
    }

    private void Drop()
    {
        ReleaseCarryJoint();

        if (Carried is { IsRemoved: false } carried)
        {
            // Lets go rather than throws: the body keeps the velocity it was carried with - the
            // player's own motion plus whatever a view flick added - which the carry's speed cap
            // already bounds
            carried.EndCarry();

            if (carried is PropPhysics prop)
            {
                prop.ImpactDamageScale /= CarriedImpactDamageScale;
            }
        }

        carryStrainTime = 0f;
        Carried = null;
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

        Drop();

        if (hasPresenceBody)
        {
            presenceBody.Destroy();
            hasPresenceBody = false;
        }
    }
}
