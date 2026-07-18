namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a model animation: a named, fixed-rate sequence of skeleton frames. Implemented by
    /// <see cref="SequenceAnimation"/> (legacy ANIM/ASEQ sequences decoded directly on the model
    /// skeleton) and <see cref="ClipAnimation"/> (animation graph clips targeting a separate NM
    /// skeleton, retargeted by bone name).
    /// </summary>
    public abstract class Animation
    {
        /// <summary>
        /// Gets the name of the animation.
        /// </summary>
        public string Name { get; protected init; } = string.Empty;

        /// <summary>
        /// Gets the frames per second of the animation.
        /// </summary>
        public float Fps { get; protected init; }

        /// <summary>
        /// Gets the total number of frames in the animation.
        /// </summary>
        public int FrameCount { get; protected init; }

        /// <summary>
        /// Gets or sets whether this animation is composed additively over the skeleton bind pose rather
        /// than applied as an absolute pose. For AG2 clips this comes from the clip's own additive flag;
        /// AG1 sequences have no such flag, so the model loader sets it from the animation graph.
        /// </summary>
        public bool IsAdditive { get; set; }

        /// <summary>
        /// Gets whether this animation targets a foreign NM skeleton and must be retargeted onto the
        /// model skeleton by bone name for playback or export.
        /// </summary>
        public virtual bool RequiresRetarget => false;

        /// <summary>
        /// Gets the skeleton resource name this animation targets, or <see langword="null"/> when it
        /// animates the model skeleton directly.
        /// </summary>
        public virtual string? TargetSkeletonName => null;

        /// <summary>
        /// Decodes animation data for the frame selected by <see cref="Frame.FrameIndex"/> into the
        /// given frame's bones.
        /// </summary>
        public abstract void DecodeFrame(Frame outFrame);

        /// <summary>
        /// Determines whether this animation has root motion movement data.
        /// </summary>
        public abstract bool HasMovementData();

        /// <summary>
        /// Returns interpolated root motion data at the specified time.
        /// </summary>
        public abstract AnimationMovement.MovementData GetMovementOffsetData(float time);

        /// <summary>
        /// Returns root motion data at the specified frame.
        /// </summary>
        public abstract AnimationMovement.MovementData GetMovementOffsetData(int frame);

        /// <summary>
        /// Composes an already-decoded additive frame over the skeleton bind pose, in place.
        /// </summary>
        public abstract void ComposeAdditiveOverBindPose(FrameBone[] bones, Skeleton skeleton);

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the animation name.
        /// </remarks>
        public override string ToString()
        {
            return Name;
        }
    }
}
