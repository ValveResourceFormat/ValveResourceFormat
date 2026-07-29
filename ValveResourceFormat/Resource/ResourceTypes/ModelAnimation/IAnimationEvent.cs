namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// An event on an animation timeline. Implemented by <see cref="AnimationEvent"/> for sequences and by
    /// <see cref="ModelAnimation2.NmClipEvent"/> for animation graph clips, so both can be consumed with
    /// <see cref="SampledAnimationEvents{T}"/> during playback.
    /// </summary>
    public interface IAnimationEvent
    {
        /// <summary>
        /// Gets the time in seconds from the start of the animation at which the event fires.
        /// </summary>
        float StartTime { get; }

        /// <summary>
        /// Gets the duration in seconds of the event window. Zero for events that fire at a single point in time.
        /// </summary>
        float Duration { get; }
    }
}
