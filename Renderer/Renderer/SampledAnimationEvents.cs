using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Enumerates the events whose start is crossed while playback advances over a time range, handling
    /// loop wrap-around. Used by <see cref="AnimationPlayer"/> as it advances its clips; allocation free,
    /// so it can run every frame.
    /// </summary>
    /// <typeparam name="T">The event type of the animation being played.</typeparam>
    public struct SampledAnimationEvents<T> where T : IAnimationEvent
    {
        private readonly T[] events;

        /// <summary>Playback cycles wrapped into the animation timeline.</summary>
        private readonly float startCycle;
        private readonly float endCycle;

        /// <summary>Whether the range covers the whole timeline, in which case every event fires.</summary>
        private readonly bool wholeTimeline;

        /// <summary>Whether the range end is inclusive, see the finished parameter of the constructor.</summary>
        private readonly bool inclusiveEnd;

        private int index;

        /// <summary>
        /// Initializes a new instance of the <see cref="SampledAnimationEvents{T}"/> struct.
        /// </summary>
        /// <param name="events">The events of the animation.</param>
        /// <param name="duration">The duration in seconds of the animation, which the playback times are cycles of.</param>
        /// <param name="previousTime">The playback time in seconds before the update.</param>
        /// <param name="newTime">The playback time in seconds after the update.</param>
        /// <param name="finished">
        /// Marks the final update of a non-looping animation: the range is treated as closed so events authored
        /// at the exact end still fire, as the end is clamped to the last frame, which a half-open range would
        /// exclude forever.
        /// </param>
        public SampledAnimationEvents(T[] events, float duration, float previousTime, float newTime, bool finished)
        {
            // Nothing can fire on a still animation, or when playback jumped backwards (a restart)
            var still = duration <= 0f;
            this.events = !still && newTime >= previousTime ? events : [];

            // Events are authored as a cycle of the animation, so playback is remapped into that space
            var toCycles = still ? 0f : 1f / duration;
            var previousCycle = previousTime * toCycles;
            var newCycle = newTime * toCycles;

            wholeTimeline = newCycle - previousCycle >= 1f;
            startCycle = Wrap(previousCycle);
            endCycle = Wrap(newCycle);
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
                if (Fires(events[index].StartCycle))
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

        private readonly bool Fires(float eventCycle)
        {
            if (wholeTimeline)
            {
                return true;
            }

            if (inclusiveEnd && eventCycle >= startCycle)
            {
                return true;
            }

            // Half-open range [startCycle, endCycle) so events at exactly zero fire when playback starts,
            // and events on the loop point are not fired twice
            return startCycle <= endCycle
                ? eventCycle >= startCycle && eventCycle < endCycle
                : eventCycle >= startCycle || eventCycle < endCycle;
        }

        private static float Wrap(float cycle)
        {
            var wrapped = cycle % 1f;
            return wrapped < 0f ? wrapped + 1f : wrapped;
        }
    }
}
