using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a model animation backed by an animation graph clip.
    /// </summary>
    public sealed class ClipAnimation : Animation
    {
        /// <summary>
        /// Gets the animation clip data for ModelAnimation2 format.
        /// </summary>
        public AnimationClip Clip { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipAnimation"/> class from an animation clip.
        /// </summary>
        public ClipAnimation(AnimationClip clip)
        {
            Name = clip.Name;
            FrameCount = clip.NumFrames;
            Fps = clip.Duration > 0 ? clip.NumFrames / clip.Duration : 1;

            Clip = clip;
        }

        /// <inheritdoc/>
        public override bool IsAdditive => Clip.IsAdditive;

        /// <inheritdoc/>
        public override void DecodeFrame(Frame outFrame)
        {
            Clip.ReadFrame(outFrame.FrameIndex, outFrame.Bones);
        }

        /// <inheritdoc/>
        public override bool HasMovementData() => false;

        /// <inheritdoc/>
        public override AnimationMovement.MovementData GetMovementOffsetData(float time) => new();

        /// <inheritdoc/>
        public override AnimationMovement.MovementData GetMovementOffsetData(int frame) => new();
    }
}
