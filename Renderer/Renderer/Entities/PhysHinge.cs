using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>phys_hinge</c> and <c>phys_hinge_local</c>: lets two bodies turn about one axis within a range of degrees.</summary>
public sealed class PhysHinge : PhysConstraint
{
    private readonly bool axisIsLocal;

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

        var definition = RevoluteJointDefinition.Default with
        {
            Base = connection,

            // An empty range means unlimited
            LimitsEnabled = minRotation != maxRotation,
            LowerAngle = float.DegreesToRadians(MathF.Min(minRotation, maxRotation) - initialRotation),
            UpperAngle = float.DegreesToRadians(MathF.Max(minRotation, maxRotation) - initialRotation),

            MotorEnabled = true,
            MotorSpeed = 0f,
            MaxMotorTorque = FrictionTorque(friction, connection),
        };

        return world.CreateRevoluteJoint(definition).AsJoint;
    }
}
