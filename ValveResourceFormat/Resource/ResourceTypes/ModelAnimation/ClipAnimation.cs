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
            // NumFrames samples span Duration, so the frame rate counts the intervals between them.
            Fps = clip.Duration > 0 && clip.NumFrames > 1 ? (clip.NumFrames - 1) / clip.Duration : 1;
            IsAdditive = clip.IsAdditive;
            TargetSkeletonName = clip.SkeletonName;

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

        /// <summary>
        /// A decoded clip bone is already the delta: clips store one for every bone, un-animated ones
        /// included, and their scale is authored around zero.
        /// </summary>
        public override FrameBone GetAdditiveDelta(int boneIndex, FrameBone bone) => bone;

        /// <inheritdoc/>
        public override void DecodeFrame(Frame outFrame)
        {
            Clip.ReadFrame(outFrame.FrameIndex, outFrame.Bones);
        }

        /// <inheritdoc/>
        public override bool HasMovementData() => Clip.RootMotion.Length > 0;

        /// <inheritdoc/>
        public override AnimationMovement.MovementData GetMovementOffsetData(float time)
        {
            var movements = Clip.RootMotion;
            if (movements.Length == 0)
            {
                return new();
            }

            var frame = time * Fps % FrameCount;
            var lower = Math.Clamp((int)MathF.Floor(frame), 0, movements.Length - 1);
            var upper = Math.Min(lower + 1, movements.Length - 1);

            return AnimationMovement.Lerp(movements[lower], movements[upper], frame - lower);
        }

        /// <inheritdoc/>
        public override AnimationMovement.MovementData GetMovementOffsetData(int frame)
        {
            var movements = Clip.RootMotion;
            if (movements.Length == 0)
            {
                return new();
            }

            return movements[Math.Clamp(frame, 0, movements.Length - 1)];
        }
    }
}
