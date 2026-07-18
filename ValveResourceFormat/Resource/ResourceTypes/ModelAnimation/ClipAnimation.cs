using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// An animation graph (NM) clip animation. Frames target the clip's own NM skeleton and are
    /// retargeted onto the model skeleton by bone name for playback and export.
    /// </summary>
    public sealed class ClipAnimation : Animation
    {
        /// <summary>
        /// Gets the underlying animation clip data block.
        /// </summary>
        public AnimationClip Clip { get; }

        // Per-frame accumulated root motion parsed from the clip's m_rootMotion.
        private readonly AnimationMovement.MovementData[] movements;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipAnimation"/> class from an animation clip.
        /// </summary>
        public ClipAnimation(AnimationClip clip)
        {
            Name = clip.Name;
            FrameCount = clip.NumFrames;

            // NumFrames samples span Duration, so the frame rate counts the intervals between them.
            Fps = clip.Duration > 0 && clip.NumFrames > 1 ? (clip.NumFrames - 1) / clip.Duration : 1;

            Clip = clip;
            IsAdditive = clip.IsAdditive;
            movements = clip.RootMotion;
        }

        /// <inheritdoc/>
        public override bool RequiresRetarget => true;

        /// <inheritdoc/>
        public override string? TargetSkeletonName => Clip.SkeletonName;

        /// <inheritdoc/>
        public override void DecodeFrame(Frame outFrame)
        {
            Clip.ReadFrame(outFrame.FrameIndex, outFrame.Bones);
        }

        /// <inheritdoc/>
        public override bool HasMovementData()
        {
            return movements.Length > 0;
        }

        /// <inheritdoc/>
        public override AnimationMovement.MovementData GetMovementOffsetData(float time)
        {
            if (movements.Length == 0)
            {
                return new();
            }

            var frame = time * Fps % FrameCount;
            var lower = Math.Clamp((int)MathF.Floor(frame), 0, movements.Length - 1);
            var upper = Math.Min(lower + 1, movements.Length - 1);
            var a = movements[lower];
            var b = movements[upper];

            return new AnimationMovement.MovementData(
                Vector3.Lerp(a.Position, b.Position, frame - lower),
                float.Lerp(a.Angle, b.Angle, frame - lower));
        }

        /// <inheritdoc/>
        public override AnimationMovement.MovementData GetMovementOffsetData(int frame)
        {
            if (movements.Length == 0)
            {
                return new();
            }

            return movements[Math.Clamp(frame, 0, movements.Length - 1)];
        }

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
    }
}
