namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Enumerates the events whose start time is crossed while playback advances over a time range,
    /// handling loop wrap-around. Obtained from <see cref="SequenceAnimation.SampleEvents"/> or
    /// <see cref="ClipAnimation.SampleEvents"/>; allocation free, so it can run every frame.
    /// </summary>
    /// <typeparam name="T">The event type of the animation being played.</typeparam>
    public struct SampledAnimationEvents<T> where T : IAnimationEvent
    {
        private readonly T[] events;

        /// <summary>Playback times wrapped into the animation timeline.</summary>
        private readonly float startTime;
        private readonly float endTime;

        /// <summary>Whether the range covers the whole timeline, in which case every event fires.</summary>
        private readonly bool wholeTimeline;

        /// <summary>Whether the range end is inclusive, see the finished parameter of the constructor.</summary>
        private readonly bool inclusiveEnd;

        private int index;

        /// <summary>
        /// Initializes a new instance of the <see cref="SampledAnimationEvents{T}"/> struct.
        /// </summary>
        /// <param name="events">The events of the animation, with times in seconds.</param>
        /// <param name="duration">The duration in seconds of the animation timeline, used to wrap looping playback.</param>
        /// <param name="previousTime">The playback time in seconds before the update.</param>
        /// <param name="newTime">The playback time in seconds after the update.</param>
        /// <param name="finished">
        /// Marks the final update of a non-looping animation: the range is treated as closed so events authored
        /// at the exact end still fire, as the end time is clamped to the last frame, which a half-open range
        /// would exclude forever.
        /// </param>
        internal SampledAnimationEvents(T[] events, float duration, float previousTime, float newTime, bool finished)
        {
            // Nothing can fire on a still animation, or when playback jumped backwards (a restart)
            this.events = duration > 0f && newTime >= previousTime ? events : [];

            wholeTimeline = newTime - previousTime >= duration;
            startTime = Wrap(previousTime, duration);
            endTime = Wrap(newTime, duration);
            inclusiveEnd = finished;
            index = -1;
        }

        /// <summary>
        /// Gets the event that fired.
        /// </summary>
        public readonly T Current => events[index];

        /// <summary>
        /// Advances to the next event that fired in this range.
        /// </summary>
        public bool MoveNext()
        {
            while (++index < events.Length)
            {
                if (Fires(events[index].StartTime))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns this enumerator, so the fired events can be iterated with foreach.
        /// </summary>
        public readonly SampledAnimationEvents<T> GetEnumerator() => this;

        private readonly bool Fires(float eventTime)
        {
            if (wholeTimeline)
            {
                return true;
            }

            if (inclusiveEnd && eventTime >= startTime)
            {
                return true;
            }

            // Half-open range [startTime, endTime) so events at exactly zero fire when playback starts,
            // and events on the loop point are not fired twice
            return startTime <= endTime
                ? eventTime >= startTime && eventTime < endTime
                : eventTime >= startTime || eventTime < endTime;
        }

        private static float Wrap(float time, float duration)
        {
            var wrapped = time % duration;
            return wrapped < 0f ? wrapped + duration : wrapped;
        }
    }
}
