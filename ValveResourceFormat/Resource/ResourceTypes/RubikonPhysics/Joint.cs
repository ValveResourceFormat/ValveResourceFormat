using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics
{
    /// <summary>
    /// Flags on a <see cref="Joint"/>.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/VPhysXJoint_t::Flags_t">VPhysXJoint_t::Flags_t</seealso>
    [Flags]
    public enum JointFlags
    {
#pragma warning disable CS1591
        None = 0,
        Body1Fixed = 1,
        UseBlockSolver = 2,
#pragma warning restore CS1591
    }

    /// <summary>
    /// The kind of constraint a <see cref="Joint"/> applies between its two bodies. Not schema-enumerated;
    /// recovered by compiling one instance of each ModelDoc joint node class and reading back <c>m_nType</c>.
    /// </summary>
#pragma warning disable CA1027
    public enum JointType
    {
#pragma warning disable CS1591
        Null = 0,
        Spherical = 1,
        Prismatic = 2,
        Revolute = 3,
        Conical = 4,
        Weld = 6,
        Wheel = 16,
#pragma warning restore CS1591
    }
#pragma warning restore CA1027

    /// <summary>
    /// Represents a constraint between two physics bodies.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/VPhysXJoint_t">VPhysXJoint_t</seealso>
    public readonly struct Joint
    {
        /// <summary>
        /// A one-dimensional motion limit.
        /// </summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/VPhysXRange_t">VPhysXRange_t</seealso>
        public readonly struct Range
        {
            /// <summary>The lower bound.</summary>
            public float Min { get; }
            /// <summary>The upper bound.</summary>
            public float Max { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="Range"/> struct.
            /// </summary>
            public Range(KVObject data)
            {
                Min = data.GetFloatProperty("m_flMin");
                Max = data.GetFloatProperty("m_flMax");
            }
        }

        /// <summary>
        /// Gets the constraint type.
        /// </summary>
        public JointType Type { get; }
        /// <summary>
        /// Index into <see cref="PhysAggregateData.Parts"/> for the first body.
        /// </summary>
        public int Body1 { get; }
        /// <summary>
        /// Index into <see cref="PhysAggregateData.Parts"/> for the second body.
        /// </summary>
        public int Body2 { get; }
        /// <summary>
        /// Gets the joint flags.
        /// </summary>
        public JointFlags Flags { get; }
        /// <summary>
        /// The joint frame local to the first body, as (position, scale, rotation).
        /// </summary>
        public (Vector3 Position, float Scale, Quaternion Rotation) Frame1 { get; }
        /// <summary>
        /// The joint frame local to the second body, as (position, scale, rotation).
        /// </summary>
        public (Vector3 Position, float Scale, Quaternion Rotation) Frame2 { get; }
        /// <summary>
        /// Gets whether the two connected bodies still collide with each other.
        /// </summary>
        public bool EnableCollision { get; }
        /// <summary>
        /// Gets whether the linear degrees of freedom are locked out entirely.
        /// </summary>
        public bool IsLinearConstraintDisabled { get; }
        /// <summary>
        /// Gets whether the angular degrees of freedom are locked out entirely.
        /// </summary>
        public bool IsAngularConstraintDisabled { get; }
        /// <summary>
        /// Gets whether <see cref="LinearLimit"/> is enforced.
        /// </summary>
        public bool EnableLinearLimit { get; }
        /// <summary>
        /// Gets the linear motion limit along the joint axis.
        /// </summary>
        public Range LinearLimit { get; }
        /// <summary>
        /// Gets whether the linear motor is enabled.
        /// </summary>
        public bool EnableLinearMotor { get; }
        /// <summary>
        /// Gets the target linear velocity driven by the linear motor.
        /// </summary>
        public Vector3 LinearTargetVelocity { get; }
        /// <summary>
        /// Gets the maximum force the linear motor may apply.
        /// </summary>
        public float MaxForce { get; }
        /// <summary>
        /// Gets whether <see cref="SwingLimit"/> is enforced.
        /// </summary>
        public bool EnableSwingLimit { get; }
        /// <summary>
        /// Gets the swing cone limit.
        /// </summary>
        public Range SwingLimit { get; }
        /// <summary>
        /// Gets whether <see cref="TwistLimit"/> is enforced.
        /// </summary>
        public bool EnableTwistLimit { get; }
        /// <summary>
        /// Gets the twist limit around the joint axis.
        /// </summary>
        public Range TwistLimit { get; }
        /// <summary>
        /// Gets whether the angular motor is enabled.
        /// </summary>
        public bool EnableAngularMotor { get; }
        /// <summary>
        /// Gets the target angular velocity driven by the angular motor.
        /// </summary>
        public Vector3 AngularTargetVelocity { get; }
        /// <summary>
        /// Gets the maximum torque the angular motor may apply.
        /// </summary>
        public float MaxTorque { get; }
        /// <summary>
        /// Gets the linear spring frequency.
        /// </summary>
        public float LinearFrequency { get; }
        /// <summary>
        /// Gets the linear spring damping ratio.
        /// </summary>
        public float LinearDampingRatio { get; }
        /// <summary>
        /// Gets the angular spring frequency.
        /// </summary>
        public float AngularFrequency { get; }
        /// <summary>
        /// Gets the angular spring damping ratio.
        /// </summary>
        public float AngularDampingRatio { get; }
        /// <summary>
        /// Gets the joint friction.
        /// </summary>
        public float Friction { get; }
        /// <summary>
        /// Gets the joint elasticity (restitution).
        /// </summary>
        public float Elasticity { get; }
        /// <summary>
        /// Gets the elastic damping.
        /// </summary>
        public float ElasticDamping { get; }
        /// <summary>
        /// Gets the plasticity, how much the joint drives back to its bind pose.
        /// </summary>
        public float Plasticity { get; }
        /// <summary>
        /// Gets an editor label for this joint.
        /// </summary>
        public string? Tag { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Joint"/> struct.
        /// </summary>
        public Joint(KVObject data)
        {
            Type = (JointType)data.GetInt32Property("m_nType");
            Body1 = data.GetInt32Property("m_nBody1");
            Body2 = data.GetInt32Property("m_nBody2");
            Flags = (JointFlags)data.GetInt32Property("m_nFlags");
            Frame1 = data.GetSubCollection("m_Frame1").ToTransform();
            Frame2 = data.GetSubCollection("m_Frame2").ToTransform();
            EnableCollision = data.GetBooleanProperty("m_bEnableCollision");
            IsLinearConstraintDisabled = data.GetBooleanProperty("m_bIsLinearConstraintDisabled");
            IsAngularConstraintDisabled = data.GetBooleanProperty("m_bIsAngularConstraintDisabled");
            EnableLinearLimit = data.GetBooleanProperty("m_bEnableLinearLimit");
            LinearLimit = new Range(data.GetSubCollection("m_LinearLimit"));
            EnableLinearMotor = data.GetBooleanProperty("m_bEnableLinearMotor");
            LinearTargetVelocity = data.GetSubCollection("m_vLinearTargetVelocity").ToVector3();
            MaxForce = data.GetFloatProperty("m_flMaxForce");
            EnableSwingLimit = data.GetBooleanProperty("m_bEnableSwingLimit");
            SwingLimit = new Range(data.GetSubCollection("m_SwingLimit"));
            EnableTwistLimit = data.GetBooleanProperty("m_bEnableTwistLimit");
            TwistLimit = new Range(data.GetSubCollection("m_TwistLimit"));
            EnableAngularMotor = data.GetBooleanProperty("m_bEnableAngularMotor");
            AngularTargetVelocity = data.GetSubCollection("m_vAngularTargetVelocity").ToVector3();
            MaxTorque = data.GetFloatProperty("m_flMaxTorque");
            LinearFrequency = data.GetFloatProperty("m_flLinearFrequency");
            LinearDampingRatio = data.GetFloatProperty("m_flLinearDampingRatio");
            AngularFrequency = data.GetFloatProperty("m_flAngularFrequency");
            AngularDampingRatio = data.GetFloatProperty("m_flAngularDampingRatio");
            Friction = data.GetFloatProperty("m_flFriction");
            Elasticity = data.GetFloatProperty("m_flElasticity");
            ElasticDamping = data.GetFloatProperty("m_flElasticDamping");
            Plasticity = data.GetFloatProperty("m_flPlasticity");
            Tag = data.GetStringProperty("m_Tag");
        }
    }
}
