using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>phys_ragdollconstraint</c>: pins two bodies at a point, limiting rotation about each axis of the entity.</summary>
public sealed class PhysRagdollConstraint : PhysConstraint
{
    private readonly record struct AxisLimit(float Min, float Max)
    {
        public bool IsLocked => Min == Max;
        public bool IsFree => Max - Min >= 360f;
    }

    /// <summary>Initializes a ragdoll constraint from its keyvalues.</summary>
    public PhysRagdollConstraint(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    private AxisLimit[] Limits =>
    [
        new(KeyValues.GetFloatProperty("xmin", -90f), KeyValues.GetFloatProperty("xmax", 90f)),
        new(KeyValues.GetFloatProperty("ymin"), KeyValues.GetFloatProperty("ymax")),
        new(KeyValues.GetFloatProperty("zmin"), KeyValues.GetFloatProperty("zmax")),
    ];

    // The one axis that turns while the other two are locked, or -1
    private int HingeAxis
    {
        get
        {
            var limits = Limits;
            var turning = Array.FindAll([0, 1, 2], i => !limits[i].IsLocked);
            return turning.Length == 1 ? turning[0] : -1;
        }
    }

    // A hinge turns about its axis, a ball socket twists about X
    /// <inheritdoc/>
    protected override Vector3 JointAxis
    {
        get
        {
            var axis = HingeAxis switch
            {
                1 => Vector3.UnitY,
                2 => Vector3.UnitZ,
                _ => Vector3.UnitX,
            };

            return Vector3.Transform(axis, EntityTransformHelper.EulerAnglesToQuaternion(Angles));
        }
    }

    /// <inheritdoc/>
    protected override Joint? CreateJoint(Box3D.PhysicsWorld world, JointDefinition connection)
    {
        var limits = Limits;

        if (Array.TrueForAll(limits, static limit => limit.IsLocked))
        {
            return world.CreateWeldJoint(WeldJointDefinition.Default with { Base = connection }).AsJoint;
        }

        var friction = MathF.Max(KeyValues.GetFloatProperty("xfriction"), MathF.Max(KeyValues.GetFloatProperty("yfriction"), KeyValues.GetFloatProperty("zfriction")));
        var frictionTorque = FrictionTorque(friction, connection);

        if (HingeAxis is >= 0 and var hinge)
        {
            var limit = limits[hinge];

            return world.CreateRevoluteJoint(RevoluteJointDefinition.Default with
            {
                Base = connection,
                LimitsEnabled = !limit.IsFree,
                LowerAngle = float.DegreesToRadians(limit.Min),
                UpperAngle = float.DegreesToRadians(limit.Max),
                MotorEnabled = true,
                MotorSpeed = 0f,
                MaxMotorTorque = frictionTorque,
            }).AsJoint;
        }

        var twist = limits[0];
        var swing = MathF.Max(SwingReach(limits[1]), SwingReach(limits[2]));

        return world.CreateSphericalJoint(SphericalJointDefinition.Default with
        {
            Base = connection,
            TwistLimitEnabled = !twist.IsFree,
            LowerTwistAngle = float.DegreesToRadians(MathF.Max(twist.Min, -178f)),
            UpperTwistAngle = float.DegreesToRadians(MathF.Min(twist.Max, 178f)),
            ConeLimitEnabled = !limits[1].IsFree && !limits[2].IsFree,
            ConeAngle = float.DegreesToRadians(MathF.Min(swing, 180f)),
            MotorEnabled = true,
            MotorVelocity = Vector3.Zero,
            MaxMotorTorque = frictionTorque,
        }).AsJoint;
    }

    // The cone is symmetric, so it reaches as far as the wider side of either swing axis
    private static float SwingReach(AxisLimit limit) => MathF.Max(MathF.Abs(limit.Min), MathF.Abs(limit.Max));
}
