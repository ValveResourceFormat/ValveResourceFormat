using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>phys_hinge</c> and <c>phys_hinge_local</c>: lets two bodies turn about one axis within a range of degrees.
/// A press on the swinging body runs it to the far end of its range, as a cabinet door opens or shuts.
/// </summary>
public sealed class PhysHinge : PhysConstraint
{
    // How hard a press swings the body, in radians per second squared at its centre of mass
    private const float DriveAcceleration = 40f;

    // An unlimited hinge swings this far from where it spawned on a press
    private const float UnlimitedSwing = 90f;

    private readonly bool axisIsLocal;

    private RevoluteJoint hinge;
    private bool hasHinge;
    private Body swingingBody;
    private float frictionTorque;

    /// <summary>Initializes a hinge from its keyvalues.</summary>
    /// <param name="system">The owning entity system.</param>
    /// <param name="spawnInfo">The spawn keyvalues.</param>
    /// <param name="axisIsLocal">Whether <c>hingeaxis</c> is in entity space, as for <c>phys_hinge_local</c>, rather than a world point.</param>
    public PhysHinge(EntitySystem system, EntitySpawnInfo spawnInfo, bool axisIsLocal) : base(system, spawnInfo)
    {
        this.axisIsLocal = axisIsLocal;
    }

    /// <inheritdoc/>
    protected override Vector3 JointAxis => TryGetAxisLine(out _, out var direction) ? direction : base.JointAxis;

    /// <inheritdoc/>
    protected override Vector3 JointAnchor
    {
        get
        {
            // Any point on the axis line works, take the one nearest the origin
            if (!TryGetAxisLine(out var point, out var direction) || direction.LengthSquared() < 1e-8f)
            {
                return base.JointAnchor;
            }

            var unit = Vector3.Normalize(direction);
            return point + unit * Vector3.Dot(Origin - point, unit);
        }
    }

    // Local hinges author two entity space points as "x y z, x y z"
    private bool TryGetAxisLine(out Vector3 point, out Vector3 direction)
    {
        point = Origin;
        direction = default;

        var text = KeyValues.GetStringProperty("hingeaxis") ?? string.Empty;

        if (!axisIsLocal)
        {
            if (!EntityTransformHelper.TryParseVector3(text.Trim(), out var target))
            {
                return false;
            }

            direction = target - Origin;
            return true;
        }

        var comma = text.IndexOf(',', StringComparison.Ordinal);

        if (comma < 0
            || !EntityTransformHelper.TryParseVector3(text[..comma].Trim(), out var start)
            || !EntityTransformHelper.TryParseVector3(text[(comma + 1)..].Trim(), out var end))
        {
            return false;
        }

        var rotation = EntityTransformHelper.EulerAnglesToQuaternion(Angles);
        point = Origin + Vector3.Transform(start, rotation);
        direction = Vector3.Transform(end - start, rotation);
        return true;
    }

    /// <inheritdoc/>
    protected override Joint? CreateJoint(Box3D.PhysicsWorld world, JointDefinition connection)
    {
        var minRotation = KeyValues.GetFloatProperty("min_rotation");
        var maxRotation = KeyValues.GetFloatProperty("max_rotation");
        var friction = KeyValues.GetFloatProperty("hingefriction");

        // Limits are measured from the spawn pose, which is at initial_rotation
        var initialRotation = KeyValues.GetFloatProperty("initial_rotation");
        var lower = MathF.Min(minRotation, maxRotation) - initialRotation;
        var upper = MathF.Max(minRotation, maxRotation) - initialRotation;

        // The limits are the swing of the hinged body. The joint measures attach2 against attach1, which turns
        // the other way when attach1 is the one that swings, as a cabinet door hinged to the world is.
        if (connection.BodyA.Type == BodyType.Dynamic)
        {
            (lower, upper) = (-upper, -lower);
        }

        var definition = RevoluteJointDefinition.Default with
        {
            Base = connection,

            // An empty range means unlimited
            LimitsEnabled = minRotation != maxRotation,
            LowerAngle = float.DegreesToRadians(lower),
            UpperAngle = float.DegreesToRadians(upper),

            MotorEnabled = true,
            MotorSpeed = 0f,
            MaxMotorTorque = FrictionTorque(friction, connection),
        };

        hinge = world.CreateRevoluteJoint(definition);
        hasHinge = true;
        swingingBody = connection.BodyA.Type == BodyType.Dynamic && connection.BodyB.Type != BodyType.Dynamic ? connection.BodyA : connection.BodyB;
        frictionTorque = definition.MaxMotorTorque;

        return hinge.AsJoint;
    }

    /// <inheritdoc/>
    protected override DriveTuning Tuning => new(Speed: 120f, ApproachRate: 15f, ArriveTolerance: 0.5f, StallProgress: 0.05f);

    /// <inheritdoc/>
    protected override bool TryGetTravel(Body body, out float position, out float firstEnd, out float secondEnd)
    {
        position = firstEnd = secondEnd = 0f;

        if (!hasHinge || !swingingBody.Equals(body) || body.Type != BodyType.Dynamic)
        {
            return false;
        }

        position = float.RadiansToDegrees(hinge.Angle);

        if (hinge.LimitsEnabled)
        {
            firstEnd = float.RadiansToDegrees(hinge.LowerAngle);
            secondEnd = float.RadiansToDegrees(hinge.UpperAngle);
        }
        else
        {
            secondEnd = position < 0f ? -UnlimitedSwing : UnlimitedSwing;
        }

        return true;
    }

    /// <inheritdoc/>
    protected override void SetDriveSpeed(float? speed)
    {
        if (speed is not { } driveSpeed)
        {
            hinge.MotorSpeed = 0f;
            hinge.MaxMotorTorque = frictionTorque;
            return;
        }

        // Enough to lift the body against gravity too, as a toilet seat swings up. Kept awake, or a body slowing
        // near its goal falls asleep short of it.
        var reach = MathF.Max(Vector3.Distance(swingingBody.CenterOfMass, JointAnchor), 1f);
        hinge.MaxMotorTorque = swingingBody.Mass * reach * (reach * DriveAcceleration + PhysicsSimulation.GravityValue);
        hinge.MotorSpeed = float.DegreesToRadians(driveSpeed);
        swingingBody.IsAwake = true;
    }
}
