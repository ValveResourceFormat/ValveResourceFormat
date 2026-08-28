namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Drives a morph from how far one bone is turned away from another. CS2 agents use it to open and
    /// close the eyelids as the eye looks up and down, which no animation channel carries.
    /// </summary>
    public class DotToMorphConstraint
    {
        /// <summary>Gets or sets the bone whose facing is measured.</summary>
        public string BoneName { get; set; } = string.Empty;

        /// <summary>Gets or sets the bone it is measured against.</summary>
        public string TargetBoneName { get; set; } = string.Empty;

        /// <summary>Gets or sets the flex controller this drives.</summary>
        public string MorphChannelName { get; set; } = string.Empty;

        /// <summary>Gets or sets the angle in degrees that maps to <see cref="OutputMin"/>.</summary>
        public float InputMin { get; set; }

        /// <summary>Gets or sets the angle in degrees that maps to <see cref="OutputMax"/>.</summary>
        public float InputMax { get; set; }

        /// <summary>Gets or sets the controller value the angle at <see cref="InputMin"/> produces.</summary>
        public float OutputMin { get; set; }

        /// <summary>Gets or sets the controller value the angle at <see cref="InputMax"/> produces.</summary>
        public float OutputMax { get; set; }

        /// <summary>Index of <see cref="BoneName"/> in the skeleton, or -1 when it is not in this model.</summary>
        public int BoneIndex { get; set; } = -1;

        /// <summary>Index of <see cref="TargetBoneName"/> in the skeleton, or -1 when it is not in this model.</summary>
        public int TargetBoneIndex { get; set; } = -1;

        /// <summary>Index of the flex controller this drives, or -1 when the morph set has no such control.</summary>
        public int MorphChannelIndex { get; set; } = -1;
    }
}
