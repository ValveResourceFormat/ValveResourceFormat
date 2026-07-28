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
        /// Gets a value indicating whether this animation is additive.
        /// </summary>
        public virtual bool IsAdditive => false;

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
