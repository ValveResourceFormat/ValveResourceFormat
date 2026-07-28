namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a model animation that can be sampled per frame.
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
        /// Gets the duration of the animation in seconds, which is also the period looping playback wraps around.
        /// </summary>
        public virtual float Duration => Fps > 0f ? FrameCount / Fps : 0f;

        /// <summary>
        /// Gets or sets whether this animation is additive: its frames are per-bone deltas meant to be
        /// composed over a base pose. AG2 clips and AG1 sequences both carry what their own data says; the
        /// owning model then adds the sequences its AG1 graph feeds into additive slots, which is not
        /// knowable from the animation alone.
        /// </summary>
        public bool IsAdditive { get; set; }

        /// <summary>
        /// Gets whether the mixer may blend this animation additively over other clips.
        /// </summary>
        public virtual bool SupportsMixerAdditive => false;

        /// <summary>
        /// Gets whether this animation is authored on a different skeleton and must be retargeted
        /// onto the model skeleton to play or export on it.
        /// </summary>
        public virtual bool RequiresRetarget => false;

        /// <summary>
        /// Gets the resource name of the skeleton this animation is authored on, or
        /// <see langword="null"/> when it is the model's own skeleton.
        /// </summary>
        public virtual string? TargetSkeletonName => null;

        /// <summary>
        /// Composes an already-decoded additive frame over the skeleton bind pose, in place.
        /// </summary>
        public abstract void ComposeAdditiveOverBindPose(FrameBone[] bones, Skeleton skeleton);

        /// <summary>
        /// Decodes animation data for the specified frame.
        /// </summary>
        public abstract void DecodeFrame(Frame outFrame);

        /// <summary>
        /// Determines whether this animation has movement data.
        /// </summary>
        public abstract bool HasMovementData();

        /// <summary>
        /// Returns interpolated root motion data at the specified time.
        /// </summary>
        public abstract AnimationMovement.MovementData GetMovementOffsetData(float time);

        /// <summary>
        /// Returns interpolated root motion data at the specified frame.
        /// </summary>
        public abstract AnimationMovement.MovementData GetMovementOffsetData(int frame);

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
