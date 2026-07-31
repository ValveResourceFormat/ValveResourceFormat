namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a tilt-twist constraint that controls bone rotation based on target bones.
    /// </summary>
    public class TiltTwistConstraint
    {
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
