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
        /// Enumerates the events whose start time is crossed while playback advances from
        /// <paramref name="previousTime"/> to <paramref name="newTime"/>, handling loop wrap-around.
        /// </summary>
        /// <param name="previousTime">The playback time in seconds before the update.</param>
        /// <param name="newTime">The playback time in seconds after the update.</param>
        /// <param name="finished">Whether this is the final update of a non looping playback, see <see cref="SampledAnimationEvents{T}"/>.</param>
        public SampledAnimationEvents<NmClipEvent> SampleEvents(float previousTime, float newTime, bool finished = false)
            => new(Events, Duration, previousTime, newTime, finished);

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
