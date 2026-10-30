using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>prop_door_rotating_physics</c>: a door that swings as a rigid body on a hinge through its origin, driven open
/// and shut by the hinge motor and latched while closed.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/server/CPropDoorRotatingPhysics">CPropDoorRotatingPhysics</seealso>
public sealed class PropDoorRotatingPhysics : PropDoorRotating, IHandGrabbable
{
    // A latched door still rattles this far either way
    private const float LatchedLimit = 0.8f;

    // Within this of its goal a swing has arrived, inside the latch slack
    private const float ArriveTolerance = 0.5f;

    // How hard the motor accelerates the door, in radians per second squared
    private const float DriveAcceleration = 40f;

    // The swing slows over its last degrees at this many degrees per second per degree left, and is held
    // still for a moment on arrival, or it slams into the limit and rebounds off it
    private const float ApproachRate = 15f;
    private const float BrakeTime = 0.2f;

    // A swing that gains less than this many degrees over the stall time is blocked, and ends where it is
    private const float StallProgress = 0.05f;
    private const float StallTime = 0.5f;

    private Body body;
    private bool hasBody;
    private RevoluteJoint hinge;
    private bool hasHinge;

    private float limitMin;
    private float limitMax;
    private float frictionTorque;
    private float driveTorque;

    private bool isDriving;
    private float goalAngle;
    private float brakeTimeLeft;
    private float stallError;
    private float stallTimeLeft;

    /// <summary>Initializes a <c>prop_door_rotating_physics</c> from its keyvalues.</summary>
    public PropDoorRotatingPhysics(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override bool UsesMoverBody => false;

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        if (Collider is not { } collider || !IsRigidBodySimulated)
        {
            return;
        }

        if (EntitySystem.Physics.CreatePropBody(collider.PhysicsData, collider.LocalBounds, Origin,
            EntityTransformHelper.EulerAnglesToQuaternion(Angles), motionEnabled: true, startAsleep: true, owner: this) is not { } created)
        {
            return;
        }

        body = created;
        hasBody = true;

        limitMin = OpenDir == OpenDirection.BackwardOnly ? 0f : -Distance;
        limitMax = OpenDir == OpenDirection.ForwardOnly ? 0f : Distance;

        var friction = KeyValues.GetFloatProperty("friction", 0.01f);

        // The default friction is ten times lighter on the hinge
        if (friction == 0.01f)
        {
            friction = 0.001f;
        }

        var bounds = collider.LocalBounds;
        var reach = MathF.Max(bounds.Min.Length(), bounds.Max.Length());

        frictionTorque = friction * PhysicsSimulation.UnitsPerMeter * PhysicsSimulation.UnitsPerMeter;
        driveTorque = body.Mass * reach * reach * DriveAcceleration;

        // The angle reads zero shut: the world frame sits at the closed pose, and the door turns about its own up axis
        var physics = EntitySystem.Physics;
        var closedFrame = new JointFrame(Origin, EntityTransformHelper.EulerAnglesToQuaternion(ClosedAngles));

        hinge = physics.World.CreateRevoluteJoint(RevoluteJointDefinition.Default with
        {
            Base = JointDefinition.Connect(physics.WorldAnchor, body, closedFrame, JointFrame.Identity) with { CollideConnected = false },
            LimitsEnabled = true,
            LowerAngle = float.DegreesToRadians(limitMin),
            UpperAngle = float.DegreesToRadians(limitMax),
            MotorEnabled = true,
            MotorSpeed = 0f,
            MaxMotorTorque = frictionTorque,
        });
        hasHinge = true;

        // Like any constraint to the world without collision, the door is free of all static geometry
        PhysicsSimulation.SetCollidesWith(body, PhysicsSimulation.StaticCategory, collide: false);

        if (State == DoorState.Closed)
        {
            Latch();
        }
    }

    /// <inheritdoc/>
    internal override bool TryGetConstraintBody(Vector3 anchor, out Body constraintBody)
    {
        constraintBody = body;
        return hasBody;
    }

    /// <inheritdoc/>
    protected override void RotateTo(Vector3 angles)
    {
        if (!hasHinge)
        {
            base.RotateTo(angles);
            return;
        }

        var turn = Quaternion.Inverse(EntityTransformHelper.EulerAnglesToQuaternion(ClosedAngles)) * EntityTransformHelper.EulerAnglesToQuaternion(angles);
        goalAngle = float.RadiansToDegrees(EntityTransformHelper.GetTwistAngleAroundZ(turn));

        // A swing replaces any pending arrival, such as the auto close
        SetMoveDoneTime(-1f);
        Unlatch();

        var error = goalAngle - HingeAngle;

        if (MathF.Abs(error) <= ArriveTolerance)
        {
            Arrive();
            return;
        }

        isDriving = true;
        brakeTimeLeft = 0f;
        stallError = MathF.Abs(error);
        stallTimeLeft = StallTime;
        hinge.MaxMotorTorque = driveTorque;
        Drive(error);
    }

    // Kept awake, or a door slowing near its goal falls asleep short of it
    private void Drive(float error)
    {
        hinge.MotorSpeed = float.DegreesToRadians(MathF.CopySign(MathF.Min(Speed, MathF.Abs(error) * ApproachRate), error));
        body.IsAwake = true;
    }

    private float HingeAngle => float.RadiansToDegrees(hinge.Angle);

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        base.PhysicsSimulate(tickInterval);

        if (!hasBody)
        {
            return;
        }

        if (IsBroken)
        {
            DestroyBody();
            return;
        }

        if (body.IsAwake)
        {
            SetOriginAndAngles(body.Position, EntityTransformHelper.ToEulerAngles(body.Rotation));
        }

        if (brakeTimeLeft > 0f)
        {
            brakeTimeLeft -= tickInterval;

            if (brakeTimeLeft <= 0f)
            {
                hinge.MaxMotorTorque = frictionTorque;
            }
        }

        if (!isDriving)
        {
            return;
        }

        var error = goalAngle - HingeAngle;

        // Arrived, or carried past the goal by something else
        if (MathF.Abs(error) <= ArriveTolerance || error * hinge.MotorSpeed < 0f)
        {
            Arrive();
            return;
        }

        if (stallError - MathF.Abs(error) > StallProgress)
        {
            stallError = MathF.Abs(error);
            stallTimeLeft = StallTime;
        }
        else if ((stallTimeLeft -= tickInterval) <= 0f)
        {
            Arrive();
            return;
        }

        Drive(error);
    }

    private void Arrive()
    {
        isDriving = false;
        hinge.MotorSpeed = 0f;
        brakeTimeLeft = BrakeTime;

        MoveDone();

        if (State == DoorState.Closed)
        {
            Latch();
        }
    }

    /// <inheritdoc/>
    public void BeginHandGrab()
    {
        if (!hasHinge)
        {
            return;
        }

        // The hand takes over from the motor, and pulling the handle releases the latch
        isDriving = false;
        brakeTimeLeft = 0f;
        hinge.MotorSpeed = 0f;
        hinge.MaxMotorTorque = frictionTorque;
        SetMoveDoneTime(-1f);
        Unlatch();
    }

    /// <inheritdoc/>
    public void EndHandGrab()
    {
        if (!hasHinge)
        {
            return;
        }

        // Let go shut it latches, anywhere else it is open
        if (MathF.Abs(HingeAngle) <= LatchedLimit)
        {
            State = DoorState.Closed;
            Latch();
        }
        else
        {
            State = DoorState.Open;
        }
    }

    private void Latch() => hinge.SetLimits(float.DegreesToRadians(-LatchedLimit), float.DegreesToRadians(LatchedLimit));

    private void Unlatch() => hinge.SetLimits(float.DegreesToRadians(limitMin), float.DegreesToRadians(limitMax));

    /// <inheritdoc/>
    protected override bool UpdatesRenderTransformEveryFrame => hasBody;

    /// <inheritdoc/>
    protected override void UpdateRenderTransform(float fraction)
    {
        // The world steps per frame, leaving an awake body nothing to interpolate
        if (hasBody && body.IsAwake)
        {
            SetRenderTransform(body.Position, body.Rotation);
            return;
        }

        base.UpdateRenderTransform(fraction);
    }

    private void DestroyBody()
    {
        if (!hasBody)
        {
            return;
        }

        if (hasHinge && hinge.AsJoint.IsValid)
        {
            hinge.Destroy(wakeBodies: false);
        }

        hasHinge = false;
        EntitySystem.PhysicsOrNull?.Forget(body);
        body.Destroy();
        hasBody = false;
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

        DestroyBody();
    }
}
