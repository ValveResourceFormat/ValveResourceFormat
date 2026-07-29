using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Fires the events of the clips the player advances.
    /// </summary>
    public partial class AnimationPlayer
    {
        /// <summary>
        /// Raised for every event of an animation graph clip crossed while the player advances its clips.
        /// Consumers filter for the event types they handle.
        /// </summary>
        public event Action<NmClipEvent>? ClipEventFired;

        /// <summary>
        /// Raised for every event of a sequence animation crossed while the player advances its clips.
        /// Kept apart from <see cref="ClipEventFired"/> because sequence events are a struct, which handing
        /// to a delegate of their shared interface would box on every fire.
        /// </summary>
        public event Action<AnimationEvent>? SequenceEventFired;

        /// <summary>
        /// Fires the events of a clip crossed while it advanced from <paramref name="previousTime"/> to
        /// <paramref name="newTime"/>. Events of a clip blended out to zero weight are not raised.
        /// </summary>
        /// <param name="clip">The clip that advanced.</param>
        /// <param name="previousTime">The clip's playback time in seconds before the update.</param>
        /// <param name="newTime">The clip's playback time in seconds after the update.</param>
        /// <param name="finished">Whether this was the last update of a non looping clip, see <see cref="SampledAnimationEvents{T}"/>.</param>
        private void SampleEvents(PlaybackClip clip, float previousTime, float newTime, bool finished)
        {
            if (clip.Weight <= 0f)
            {
                return;
            }

            var duration = clip.Animation.Duration;

            if (clip.Animation is ClipAnimation clipAnimation)
            {
                var clipHandler = ClipEventFired;

                if (clipHandler != null)
                {
                    foreach (var clipEvent in new SampledAnimationEvents<NmClipEvent>(clipAnimation.Events, duration, previousTime, newTime, finished))
                    {
                        clipHandler(clipEvent);
                    }
                }
            }
            else if (clip.Animation is SequenceAnimation sequence)
            {
                var sequenceHandler = SequenceEventFired;

                if (sequenceHandler != null)
                {
                    foreach (var sequenceEvent in new SampledAnimationEvents<AnimationEvent>(sequence.Events, duration, previousTime, newTime, finished))
                    {
                        sequenceHandler(sequenceEvent);
                    }
                }
            }
        }
    }
}
