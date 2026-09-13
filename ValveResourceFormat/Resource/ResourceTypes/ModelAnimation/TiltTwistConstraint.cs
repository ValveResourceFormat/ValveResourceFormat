using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a tilt-twist constraint that controls bone rotation based on target bones.
    /// </summary>
    public class TiltTwistConstraint
    {
        /// <summary>
        /// Reads a model's tilt-twist constraints (<c>CTiltTwistConstraint</c>) in compiled order.
        /// </summary>
        public static TiltTwistConstraint[] ReadList(Model model)
        {
            var constraints = new List<TiltTwistConstraint>();

            foreach (var constraintData in model.GetBoneConstraints("CTiltTwistConstraint"))
            {
                var upVec = constraintData.GetFloatArray("m_vUpVector");

                var constraint = new TiltTwistConstraint
                {
                    Name = constraintData.GetStringProperty("m_name"),
                    UpVector = new Vector3(upVec[0], upVec[1], upVec[2]),
                    TargetAxis = (int)constraintData.GetIntegerProperty("m_nTargetAxis"),
                    SlaveAxis = (int)constraintData.GetIntegerProperty("m_nSlaveAxis"),
                };

                var slaves = constraintData.GetArray("m_slaves");
                constraint.Slaves = slaves.Select(s =>
                {
                    var quat = s.GetFloatArray("m_qBaseOrientation");
                    var pos = s.GetFloatArray("m_vBasePosition");

                    return new TiltTwistConstraintSlave
                    {
                        BaseOrientation = new Quaternion(quat[0], quat[1], quat[2], quat[3]),
                        BasePosition = new Vector3(pos[0], pos[1], pos[2]),
                        BoneHash = s.GetUInt32Property("m_nBoneHash"),
                        Weight = s.GetFloatProperty("m_flWeight"),
                        Name = s.GetStringProperty("m_sName"),
                    };
                }).ToArray();

                var targets = constraintData.GetArray("m_targets");
                constraint.Targets = targets.Select(t =>
                {
                    var quat = t.GetFloatArray("m_qOffset");
                    var pos = t.GetFloatArray("m_vOffset");

                    return new TiltTwistConstraintTarget
                    {
                        Offset = new Quaternion(quat[0], quat[1], quat[2], quat[3]),
                        PositionOffset = new Vector3(pos[0], pos[1], pos[2]),
                        BoneHash = t.GetUInt32Property("m_nBoneHash"),
                        Name = t.GetStringProperty("m_sName"),
                        Weight = t.GetFloatProperty("m_flWeight"),
                        IsAttachment = t.GetBooleanProperty("m_bIsAttachment"),
                    };
                }).ToArray();

                constraints.Add(constraint);
            }

            return [.. constraints];
        }

        /// <summary>
        /// Gets or sets the name of the constraint.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the up vector used for constraint calculations.
        /// </summary>
        public Vector3 UpVector { get; set; }

        /// <summary>
        /// Gets or sets the collection of slave bones affected by this constraint.
        /// </summary>
        public TiltTwistConstraintSlave[] Slaves { get; set; } = [];

        /// <summary>
        /// Gets or sets the collection of target bones that drive this constraint.
        /// </summary>
        public TiltTwistConstraintTarget[] Targets { get; set; } = [];

        /// <summary>
        /// Gets or sets the target axis index.
        /// </summary>
        public int TargetAxis { get; set; }

        /// <summary>
        /// Gets or sets the slave axis index.
        /// </summary>
        public int SlaveAxis { get; set; }
    }

    /// <summary>
    /// Represents a slave bone in a tilt-twist constraint.
    /// </summary>
    public class TiltTwistConstraintSlave
    {
        /// <summary>
        /// Gets or sets the base orientation of the slave bone.
        /// </summary>
        public Quaternion BaseOrientation { get; set; }

        /// <summary>
        /// Gets or sets the base position of the slave bone.
        /// </summary>
        public Vector3 BasePosition { get; set; }

        /// <summary>
        /// Gets or sets the bone hash identifier.
        /// </summary>
        public uint BoneHash { get; set; }

        /// <summary>
        /// Gets or sets the weight of the constraint's influence on this slave bone.
        /// </summary>
        public float Weight { get; set; }

        /// <summary>
        /// Gets or sets the name of the slave bone.
        /// </summary>
        public string? Name { get; set; }
    }

    /// <summary>
    /// Represents a target bone in a tilt-twist constraint.
    /// </summary>
    public class TiltTwistConstraintTarget
    {
        /// <summary>
        /// Gets or sets the rotation offset applied to the target.
        /// </summary>
        public Quaternion Offset { get; set; }

        /// <summary>
        /// Gets or sets the position offset applied to the target.
        /// </summary>
        public Vector3 PositionOffset { get; set; }

        /// <summary>
        /// Gets or sets the bone hash identifier.
        /// </summary>
        public uint BoneHash { get; set; }

        /// <summary>
        /// Gets or sets the name of the target bone.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the weight of this target's influence.
        /// </summary>
        public float Weight { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this target is an attachment rather than a bone.
        /// </summary>
        public bool IsAttachment { get; set; }
    }
}
