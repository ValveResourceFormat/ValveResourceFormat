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

    private const float PickupReach = 300f;
    private const float HoldDistance = 40f;

    // Capped springs yield to obstacles instead of ramming them, caps are accelerations scaled by mass
    private const float CarryHertz = 12f;
    private const float CarryDampingRatio = 1f;
    private const float MaxCarryAcceleration = 20000f;
    private const float MaxCarryAngularAcceleration = 150f;

    private const float MaxCarrySpeed = 1000f;

    // The hand travels from the body to the hold pose, pulling far props in steadily
    private const float PickupTravelSpeed = 500f;
    private const float PickupTravelBraking = 4000f;

    // Pressing a held prop into the world as it turns to fit should not shatter it
    private const float CarriedImpactDamageScale = 0.4f;

    private const float CarryBreakDistance = 64f;
    private const float CarryBreakTime = 0.5f;

    // A press on a body grabs it at once, and letting go within this long turns it into a press instead. Only
    // past it does the grab take over what the press would have done, stopping a door's motor or an interactable.
    private const float TapTime = 0.2f;

    /// <summary>Gets the controller whose state this entity reflects.</summary>
    public IPlayerController Controller { get; }

    internal BoundingBox HullBounds => BoundingBox.FromCenter(Origin + new Vector3(0f, 0f, Controller.HullHalfExtents.Z), Controller.HullHalfExtents);

    /// <summary>Gets what the player is carrying, or <see langword="null"/> when their hands are free.</summary>
    public ICarryable? Carried { get; private set; }

    // Held and changed buttons, latched once per tick
    private PlayerButtonState Buttons;

    // Kinematic stand-in for the hull, shoving props the player walks into
    private Body presenceBody;
    private bool hasPresenceBody;
    private int presenceTeleportCount;

    private const float MinShoveSpeed = 20f;
    private const float MaxShoveSpeed = 350f;
    private const float ShoveMargin = 2f;

    private Vector3 baseVelocity;

    /// <summary>Adds a push velocity applied on the next tick.</summary>
    public void AddBaseVelocity(Vector3 velocity) => baseVelocity += velocity;

    private readonly List<Body> debrisBodies = [];
    private readonly HashSet<ulong> debrisBodyIds = [];

    // A flick spikes this for a tick or two, a wedged prop keeps it climbing
    private float carryStrainTime;

    private Body carryHand;
    private MotorJoint carryJoint;
    private bool hasCarryJoint;

    private bool carryHandArrived;

    private bool isUsePending;
    private float usePressTime;

    // The aim when use went down, which a tap acts on even after the view moves away
    private Vector3 pressEyePosition;
    private Vector3 pressForward;

    private Body grabBody;
    private Body grabHand;
    private MotorJoint grabJoint;
    private bool hasGrab;
    private bool isHandGrabbing;
    private Vector3 grabLocalPoint;
    private float grabDistance;
    private float grabStrainTime;

    private IAimGrabbable? aimGrab;

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
    public BaseEntity? PressUse() => PressUse(Controller.EyePosition, Controller.ViewForward);

    private BaseEntity? PressUse(Vector3 eyePosition, Vector3 forward)
    {
        var target = EntitySystem.FindUseTarget(eyePosition, eyePosition + forward * UseRange);

        target?.Use(this);

        return target;
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        // Resets key latches inside player movement, if a key is presseed,
        // unpressed and pressed again in within 3 frames the tick will see it as one press.
        Buttons = Controller.ConsumeButtons();

        Controller.SetBaseVelocity(baseVelocity);
        baseVelocity = Vector3.Zero;

        // The controller owns the position, nothing to integrate
        SyncFromController();

        UpdateUse();

        UpdatePhysicsPresence(tickInterval);
    }

    // A press on a body grabs it until it turns out to be a tap, anything else acts at once
    private void UpdateUse()
    {
        var isHolding = Buttons.Holding(TrackedKeys.E);

        if (Buttons.Pressed(TrackedKeys.E))
        {
            pressEyePosition = Controller.EyePosition;
            pressForward = Controller.ViewForward;

            if (Carried != null)
            {
                Drop();
            }
            else if (isHolding && (FindAimGrabbable(out _) != null || FindGrabTarget(out _, out _)))
            {
                isUsePending = true;
                usePressTime = EntitySystem.CurrentTime;
                TryGrab();
            }
            else
            {
                Press();
            }

            return;
        }

        if (isHolding)
        {
            if (isUsePending && EntitySystem.CurrentTime - usePressTime >= TapTime)
            {
                isUsePending = false;
                TakeHold();
            }

            return;
        }

        ReleaseGrab();
        ReleaseAimGrab();

        if (isUsePending)
        {
            isUsePending = false;
            Press();
        }
    }

    private void Press()
    {
        if (FindAimGrabbable(out _) is BaseEntity interactable)
        {
            interactable.Use(this);
        }
        else if (PressUse(pressEyePosition, pressForward) == null && !TryPressSlide())
        {
            TryPickup();
        }
    }

    // Drawers and other sliding bodies run open or shut
    private bool TryPressSlide()
    {
        if (!FindGrabTarget(out var body, out _) || PhysSlideConstraint.FindSliding(EntitySystem, body) is not { } slide)
        {
            return false;
        }

        slide.Press();
        return true;
    }

    // The nearest interactable along the aim at the press within reach
    private IAimGrabbable? FindAimGrabbable(out float distance)
    {
        var eyePosition = pressEyePosition;
        var forward = pressForward;
        IAimGrabbable? nearest = null;
        distance = UseRange;

        foreach (var entity in EntitySystem.Entities)
        {
            if (!entity.IsRemoved && entity is IAimGrabbable grabbable && grabbable.TryAim(eyePosition, forward, distance, out var along))
            {
                nearest = grabbable;
                distance = along;
            }
        }

        return nearest;
    }

    internal void ReleaseAimGrab()
    {
        var released = aimGrab;
        aimGrab = null;
        released?.EndAimGrab();
    }

    /// <inheritdoc/>
    internal override void FrameSimulate(float frameTime)
    {
        UpdateCarry(frameTime);
        UpdateGrab(frameTime);

        if (aimGrab != null)
        {
            var (eyePosition, forward, _) = GetHoldView();
            aimGrab.UpdateAimGrab(eyePosition, forward, frameTime);
        }
    }

    private void SyncFromController()
    {
        WorldOrigin = Controller.Position;
        Velocity = Controller.Velocity;
    }

    // Moved with a velocity to shove props at the player speed, snapped on teleports
    private void UpdatePhysicsPresence(float tickInterval)
    {
        if (EntitySystem.PhysicsOrNull is not { } physics)
        {
            return;
        }

        if (!hasPresenceBody)
        {
            presenceBody = physics.CreatePlayerBody(Origin, Controller.HullHalfExtents);
            hasPresenceBody = true;
            presenceTeleportCount = Controller.TeleportCount;
            return;
        }

        if (presenceTeleportCount != Controller.TeleportCount)
        {
            presenceTeleportCount = Controller.TeleportCount;
            presenceBody.SetTransform(Origin, null);
        }
        else
        {
            presenceBody.MoveTowards(Origin, Quaternion.Identity, tickInterval, wake: true);
        }

        ShoveDebris(physics);
    }

    // Debris and ragdolls do not collide with the player body, a horizontal shove never presses them into the ground
    private void ShoveDebris(PhysicsSimulation physics)
    {
        var horizontalVelocity = Velocity with { Z = 0f };
        var speed = horizontalVelocity.Length();

        // A still player must not jitter what they stand in
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
            if (body.UserData == heldBody?.UserData)
            {
                continue;
            }

            var direction = (body.Position - center) with { Z = 0f };
            var distance = direction.Length();

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

    // The hand moves onto the hold pose and the joint pulls the body after it
    private void UpdateCarry(float deltaTime)
    {
        // A zero-length frame has no step to move the hand over
        if (deltaTime <= 1e-6f || Carried is not { } carried || !hasCarryJoint)
        {
            return;
        }

        if (carried.IsRemoved)
        {
            // Removed mid-carry, nothing to restore state on
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

        // A wedged prop is dropped, after a strain longer than a throw flick
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

    // Keeps the body rotation when only the position is held
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

        // The solver hides rotational inertia, mass at the shape reach approximates it
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

        // Destroyed with the carried body when it breaks
        if (carryJoint.AsJoint.IsValid)
        {
            carryJoint.AsJoint.Destroy();
        }

        carryHand.Destroy();
        hasCarryJoint = false;
    }

    // The drawn camera includes view smoothing and punch, a carry steered by the input camera trails it
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

        var eyePosition = pressEyePosition;
        var forward = pressForward;

        // World and movers included to block pickups through them, player category left out for debris
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

        // Adding the grab point offset from the mass center of the grabbed part keeps large props clear of the hull
        Carried = carryable;
        carryStrainTime = 0f;
        carryable.BeginCarry(this, HoldDistance + Vector3.Distance(hit.Point, grabbedBody.CenterOfMass), grabbedBody);
        CreateCarryJoint(physics, carryable, carryable.CarryBody);

        if (carryable is PropPhysics prop)
        {
            prop.ImpactDamageScale *= CarriedImpactDamageScale;
        }
    }

    // Bodies within use range a hand can take hold of
    private bool FindGrabTarget(out Body body, out Vector3 point)
    {
        body = default;
        point = default;

        if (EntitySystem.PhysicsOrNull is not { } physics)
        {
            return false;
        }

        var hit = physics.World.RaycastClosest(pressEyePosition, pressForward * UseRange,
            new QueryFilter(~PhysicsSimulation.PlayerCategory,
                PhysicsSimulation.StaticCategory | PhysicsSimulation.PropCategory
                | PhysicsSimulation.DebrisCategory | PhysicsSimulation.RagdollCategory | PhysicsSimulation.MoverCategory));

        if (!hit.Hit || hit.Shape.Body.Type != BodyType.Dynamic || physics.GetOwner(hit.Shape.Body) is not { IsRemoved: false })
        {
            return false;
        }

        body = hit.Shape.Body;
        point = hit.Point;
        return true;
    }

    // Takes a body by the aimed point, leaving what it belongs to alone until the press becomes a hold
    private void TryGrab()
    {
        if (hasGrab || FindAimGrabbable(out _) != null || !FindGrabTarget(out var body, out var point))
        {
            return;
        }

        var physics = EntitySystem.Physics;

        grabBody = body;
        grabLocalPoint = body.ToLocalPoint(point);
        grabDistance = Vector3.Distance(Controller.EyePosition, point);
        grabStrainTime = 0f;
        grabHand = physics.World.CreateKinematicBody(point, Quaternion.Identity);

        // Position only, the body turns about the grabbed point on its own constraints
        grabJoint = physics.World.CreateMotorJoint(MotorJointDefinition.Default with
        {
            Base = JointDefinition.Connect(grabHand, body, JointFrame.Identity, new JointFrame(grabLocalPoint)),
            LinearHertz = CarryHertz,
            LinearDampingRatio = CarryDampingRatio,
            MaxSpringForce = body.Mass * MaxCarryAcceleration,
            AngularHertz = 0f,
            MaxSpringTorque = 0f,
            MaxVelocityForce = 0f,
            MaxVelocityTorque = 0f,
        });
        hasGrab = true;
        body.IsAwake = true;
    }

    // An interactable starts from the aim at the press, so it catches up with how far the view has moved since
    private void TakeHold()
    {
        if (FindAimGrabbable(out var aimDistance) is { } interactable)
        {
            aimGrab = interactable;
            interactable.BeginAimGrab(this, pressEyePosition, pressForward, aimDistance);
            return;
        }

        if (!hasGrab || !grabBody.IsValid)
        {
            return;
        }

        PhysSlideConstraint.FindSliding(EntitySystem, grabBody)?.StopDrive();

        if (EntitySystem.Physics.GetOwner(grabBody) is IHandGrabbable grabbable)
        {
            isHandGrabbing = true;
            grabbable.BeginHandGrab();
        }
    }

    // The grabbed point follows the aim at the distance it was taken at
    private void UpdateGrab(float deltaTime)
    {
        if (!hasGrab || deltaTime <= 1e-6f)
        {
            return;
        }

        if (!grabBody.IsValid || !grabJoint.AsJoint.IsValid)
        {
            ReleaseGrab();
            return;
        }

        var (eyePosition, forward, _) = GetHoldView();
        var target = eyePosition + forward * grabDistance;

        grabHand.MoveTowards(target, Quaternion.Identity, deltaTime, wake: true);

        if (Vector3.Distance(grabBody.ToWorldPoint(grabLocalPoint), target) > CarryBreakDistance)
        {
            grabStrainTime += deltaTime;

            if (grabStrainTime >= CarryBreakTime)
            {
                ReleaseGrab();
            }
        }
        else
        {
            grabStrainTime = 0f;
        }
    }

    private void ReleaseGrab()
    {
        if (!hasGrab)
        {
            return;
        }

        hasGrab = false;

        if (grabJoint.AsJoint.IsValid)
        {
            grabJoint.AsJoint.Destroy();
        }

        grabHand.Destroy();

        if (isHandGrabbing && grabBody.IsValid)
        {
            (EntitySystem.PhysicsOrNull?.GetOwner(grabBody) as IHandGrabbable)?.EndHandGrab();
        }

        isHandGrabbing = false;
    }

    private void Drop()
    {
        ReleaseCarryJoint();

        if (Carried is { IsRemoved: false } carried)
        {
            // Keeps the carried velocity, already bounded by MaxCarrySpeed
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
        ReleaseGrab();
        ReleaseAimGrab();

        if (hasPresenceBody)
        {
            presenceBody.Destroy();
            hasPresenceBody = false;
        }
    }
}
