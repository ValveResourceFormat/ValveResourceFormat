using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>phys_hinge</c> and <c>phys_hinge_local</c>: lets the two bodies turn about one axis through this
/// entity's origin, within <c>min_rotation</c> and <c>max_rotation</c> degrees, against the authored
/// <c>hingefriction</c>. The plain hinge names a world point its axis runs toward; the local one gives
/// a direction in the entity's own frame, so the hinge keeps its axis when the entity is rotated.
/// </summary>
public sealed class PhysHinge : PhysConstraint
{
    private readonly bool axisIsLocal;

    /// <summary>Initializes a hinge from its keyvalues.</summary>
    /// <param name="system">The entity system the hinge belongs to.</param>
    /// <param name="spawnInfo">The hinge's keyvalues and placement.</param>
    /// <param name="axisIsLocal">Whether <c>hingeaxis</c> is a direction in the entity's frame, as for <c>phys_hinge_local</c>, rather than a world point.</param>
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
            // The hinge turns about the whole line, so the anchor is the point on it nearest the entity
            if (!TryGetAxisLine(out var point, out var direction) || direction.LengthSquared() < 1e-8f)
            {
                return base.JointAnchor;
            }

            var unit = Vector3.Normalize(direction);
            return point + unit * Vector3.Dot(Origin - point, unit);
        }
    }

    // The axis as a line in the world. The plain hinge runs it from its origin toward a world point;
    // the local one names two points on it in its own frame, "x y z, x y z".
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
    protected override Joint? CreateJoint(PhysicsWorld world, JointDefinition connection)
    {
        var minRotation = KeyValues.GetFloatProperty("min_rotation");
        var maxRotation = KeyValues.GetFloatProperty("max_rotation");
        var friction = KeyValues.GetFloatProperty("hingefriction");

        // The pose the bodies spawn in reads as the initial rotation, so the limits are measured from it
        var initialRotation = KeyValues.GetFloatProperty("initial_rotation");

        var definition = RevoluteJointDefinition.Default with
        {
            Base = connection,

            // A range of zero width is how an unlimited hinge is authored
            LimitsEnabled = minRotation != maxRotation,
            LowerAngle = float.DegreesToRadians(MathF.Min(minRotation, maxRotation) - initialRotation),
            UpperAngle = float.DegreesToRadians(MathF.Max(minRotation, maxRotation) - initialRotation),

            // Friction as a motor that wants no motion and gives up past the friction torque
            MotorEnabled = friction > 0f,
            MotorSpeed = 0f,
            MaxMotorTorque = friction * FrictionTorqueScale,
        };

        return world.CreateRevoluteJoint(definition).AsJoint;
    }
}
