using Box3D;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>phys_genericconstraint</c>: locks or frees each linear and angular axis of the entity frame, with per axis break limits.</summary>
public sealed class PhysGenericConstraint : PhysConstraint
{
    private static readonly string[] AxisNames = ["x", "y", "z"];
    private static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];

    private readonly Vector3 forceLimits;
    private readonly Vector3 torqueLimits;
    private readonly Vector3 notifyForces;
    private readonly Vector3 notifyIntervals;
    private Vector3 lastNotifyTimes = new(float.NegativeInfinity);
    private Quaternion frameInBodyA = Quaternion.Identity;

    /// <summary>Initializes a generic constraint from its keyvalues.</summary>
    public PhysGenericConstraint(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
        forceLimits = ReadPerAxis("forcelimit_") * LimitForceScale;
        torqueLimits = ReadPerAxis("torquelimit_") * LimitForceScale;
        notifyForces = ReadPerAxis("notifyforce_") * LimitForceScale;
        notifyIntervals = ReadPerAxis("notifyforcemintime_");
    }

    private Vector3 ReadPerAxis(string prefix)
        => new(KeyValues.GetFloatProperty(prefix + "x"), KeyValues.GetFloatProperty(prefix + "y"), KeyValues.GetFloatProperty(prefix + "z"));

    private bool IsLocked(string motion, int axis) => KeyValues.GetStringProperty($"{motion}_motion_{AxisNames[axis]}") == "JOINT_MOTION_LOCKED";

    private int[] FreeAxes(string motion) => Array.FindAll([0, 1, 2], i => !IsLocked(motion, i));

    private Quaternion FrameRotation => EntityTransformHelper.EulerAnglesToQuaternion(Angles);

    // A hinge or slider moves along its one free axis
    /// <inheritdoc/>
    protected override Vector3 JointAxis
    {
        get
        {
            var free = FreeAxes("angular") is [var angular] ? angular : FreeAxes("linear") is [var linear] ? linear : 2;
            return Vector3.Transform(Axes[free], FrameRotation);
        }
    }

    /// <inheritdoc/>
    protected override Joint? CreateJoint(Box3D.PhysicsWorld world, JointDefinition connection)
    {
        frameInBodyA = Quaternion.Inverse(connection.BodyA.Rotation) * FrameRotation;

        var freeLinear = FreeAxes("linear");
        var freeAngular = FreeAxes("angular");

        // Springy locked axes soften the whole joint, the solver has no per axis stiffness
        var (linearHertz, linearDamping) = Softness("linear");
        var (angularHertz, angularDamping) = Softness("angular");
        var softened = linearHertz > 0f ? connection with { ConstraintHertz = linearHertz, ConstraintDampingRatio = linearDamping } : connection;

        switch (freeLinear.Length, freeAngular.Length)
        {
            case (0, 0):
                return world.CreateWeldJoint(WeldJointDefinition.Default with
                {
                    Base = connection,
                    LinearHertz = linearHertz,
                    LinearDampingRatio = linearDamping,
                    AngularHertz = angularHertz,
                    AngularDampingRatio = angularDamping,
                }).AsJoint;

            case (0, 1):
                return world.CreateRevoluteJoint(RevoluteJointDefinition.Default with { Base = softened }).AsJoint;

            case (0, _):
                return world.CreateSphericalJoint(SphericalJointDefinition.Default with { Base = softened }).AsJoint;

            case (1, 0):
                var slider = PrismaticJointDefinition.Slider(connection.BodyA, connection.BodyB, Origin, JointAxis);
                return world.CreatePrismaticJoint(slider with { Base = slider.Base with { CollideConnected = connection.CollideConnected } }).AsJoint;

            case (3, 3):
                return null;

            default:
                EntitySystem.Logger.LogDebug("{Classname} '{TargetName}' frees {Linear} linear and {Angular} angular axes, which no joint matches", Classname, TargetName, freeLinear.Length, freeAngular.Length);
                return null;
        }
    }

    // The stiffest spring among the locked axes
    private (float Hertz, float DampingRatio) Softness(string motion)
    {
        var hertz = 0f;
        var damping = 0f;

        for (var i = 0; i < 3; i++)
        {
            var axisHertz = KeyValues.GetFloatProperty($"{motion}_frequency_{AxisNames[i]}");

            if (IsLocked(motion, i) && axisHertz > hertz)
            {
                hertz = axisHertz;
                damping = KeyValues.GetFloatProperty($"{motion}_damping_ratio_{AxisNames[i]}");
            }
        }

        return (hertz, damping);
    }

    /// <inheritdoc/>
    protected override bool IsOverloaded(Joint joint, float tickInterval)
    {
        var toFrame = Quaternion.Inverse(joint.BodyA.Rotation * frameInBodyA);
        var force = Vector3.Abs(Vector3.Transform(joint.ConstraintForce, toFrame));
        var torque = Vector3.Abs(Vector3.Transform(joint.ConstraintTorque, toFrame));

        for (var i = 0; i < 3; i++)
        {
            NotifyForce(i, force[i]);

            if ((forceLimits[i] > 0f && force[i] > forceLimits[i]) || (torqueLimits[i] > 0f && torque[i] > torqueLimits[i]))
            {
                return true;
            }
        }

        return base.IsOverloaded(joint, tickInterval);
    }

    // An interval of zero never fires, a negative one fires once
    private void NotifyForce(int axis, float force)
    {
        var interval = notifyIntervals[axis];

        if (notifyForces[axis] <= 0f || interval == 0f || force < notifyForces[axis])
        {
            return;
        }

        var hasFired = !float.IsNegativeInfinity(lastNotifyTimes[axis]);

        if (interval < 0f ? hasFired : EntitySystem.CurrentTime - lastNotifyTimes[axis] < interval)
        {
            return;
        }

        lastNotifyTimes[axis] = EntitySystem.CurrentTime;
        EntitySystem.TriggerOutput(this, "NotifyForceReached" + AxisNames[axis].ToUpperInvariant());
    }
}
