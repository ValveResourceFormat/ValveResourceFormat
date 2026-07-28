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
            IsAdditive = clip.IsAdditive;

            Clip = clip;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Taken from the clip itself, rather than derived from the frame rate that was derived from it.
        /// </remarks>
        public override float Duration => Clip.Duration;

        /// <summary>
        /// Gets the events on this animation's timeline, ordered by start time. Times are in seconds.
        /// Consumers filter for the event types they are interested in (e.g. <see cref="NmSoundEvent"/>).
        /// </summary>
        public NmClipEvent[] Events => Clip.Events;

        /// <inheritdoc/>
        public override bool SupportsMixerAdditive => IsAdditive;

        /// <summary>
        /// Composes an already-decoded additive frame over the skeleton bind pose, in place. Clips
        /// store an identity delta for un-animated bones, so every bone can be composed.
        /// </summary>
        public override void ComposeAdditiveOverBindPose(FrameBone[] bones, Skeleton skeleton)
        {
            for (var i = 0; i < bones.Length; i++)
            {
                var bindPose = new FrameBone(skeleton.Bones[i].Position, 1f, skeleton.Bones[i].Angle);
                bones[i] = bones[i].BlendAdd(bindPose, 1f);
            }
        }

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
