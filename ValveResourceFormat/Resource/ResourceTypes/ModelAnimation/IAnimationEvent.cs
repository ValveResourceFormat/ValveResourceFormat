namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// An event on an animation timeline, consumable with <see cref="SampledAnimationEvents{T}"/> during playback.
    /// Implemented by <see cref="AnimationEvent"/> for sequences and by <see cref="ModelAnimation2.NmClipEvent"/>
    /// for animation graph clips, which author their event times in different units but both carry a cycle.
    /// </summary>
    public interface IAnimationEvent
    {
        /// <summary>
        /// Gets the position of the event as a fraction of the animation, zero being its start and one its end.
        /// This is what playback is sampled against, so the two formats compare in the same space.
        /// </summary>
        float StartCycle { get; }

        /// <summary>
        /// Gets the time in seconds from the start of the animation at which the event fires.
        /// Zero for events that are not authored in seconds, see the implementation.
        /// </summary>
        float StartTime { get; }

        /// <summary>
        /// Gets the duration in seconds of the event window. Zero for events that fire at a single point in time.
        /// </summary>
        float Duration { get; }
    }
}
