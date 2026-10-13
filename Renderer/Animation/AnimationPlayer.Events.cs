using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Fires the events of the clips the player advances, and handles the ones it acts on itself.
    /// </summary>
    public partial class AnimationPlayer
    {
        /// <summary>
        /// Raised for every event of an animation graph clip crossed while the player advances its clips.
        /// Consumers filter for the event types they handle.
        /// </summary>
        public event Action<Animation.Event>? ClipEventFired;

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

                foreach (var clipEvent in new SampledAnimationEvents<Animation.Event>(clipAnimation.Events, duration, previousTime, newTime, finished))
                {
                    clipHandler?.Invoke(clipEvent);
                    PlayEventSound(clip, clipEvent, newTime, finished);
                }
            }
            else if (clip.Animation is SequenceAnimation sequence)
            {
                var sequenceHandler = SequenceEventFired;

                foreach (var sequenceEvent in new SampledAnimationEvents<AnimationEvent>(sequence.Events, duration, previousTime, newTime, finished))
                {
                    sequenceHandler?.Invoke(sequenceEvent);
                    PlayEventSound(sequenceEvent);
                }
            }
        }

        #region Sounds

        /// <summary>Gets or sets the resolver from an attachment name to a world position, an empty name being the model itself.</summary>
        public Func<string, Vector3?>? ResolvePosition { get; set; }

        private readonly record struct ActiveClipSound(
            Audio.SoundHandle Handle,
            PlaybackClip Clip,
            Animation.SoundEvent Event,
            float FireTime);

        private readonly List<ActiveClipSound> activeClipSounds = [];

        private void PlayEventSound(PlaybackClip clip, Animation.Event clipEvent, float newTime, bool finished)
        {
            if (clipEvent is not Animation.SoundEvent soundEvent || soundEvent.Relevance == Animation.EventRelevance.ServerOnly)
            {
                return;
            }

            var duration = clip.Animation.Duration;
            var currentTime = duration > 0f ? newTime % duration : newTime;

            var fireTime = finished && clipEvent.StartSeconds >= currentTime
                ? newTime
                : newTime - ((currentTime - clipEvent.StartSeconds + duration) % duration);

            // The eye position is the listener itself, play it unspatialized; the entity position plays at the entity
            var position = soundEvent.Position == Animation.SoundEvent.PositionType.EntityPos
                ? ResolvePosition?.Invoke(soundEvent.AttachmentName)
                : null;

            var handle = Sound.Play(soundEvent.Name, position);

            if (handle.IsValid && soundEvent.DurationSeconds > 0f)
            {
                activeClipSounds.Add(new ActiveClipSound(handle, clip, soundEvent, fireTime));
            }
        }

        private void PlayEventSound(AnimationEvent sequenceEvent)
        {
            var isAttachment = sequenceEvent.Name == "AE_CL_PLAYSOUND_ATTACHMENT";

            if (!isAttachment && sequenceEvent.Name != "AE_CL_PLAYSOUND")
            {
                return;
            }

            // The sound name is the "name" key of m_EventData, m_sOptions is unused for this event type
            var soundName = sequenceEvent.EventData?.GetStringProperty("name");

            if (string.IsNullOrEmpty(soundName))
            {
                return;
            }

            var attachmentName = isAttachment ? sequenceEvent.EventData!.GetStringProperty("attachment") : string.Empty;

            Sound.Play(soundName, ResolvePosition?.Invoke(attachmentName));
        }

        private void UpdateActiveClipSounds()
        {
            for (var i = activeClipSounds.Count - 1; i >= 0; i--)
            {
                var (handle, clip, soundEvent, fireTime) = activeClipSounds[i];

                if (!handle.Started)
                {
                    activeClipSounds.RemoveAt(i);
                    continue;
                }

                // Clip removed, blended out, or restarted (time jumped backwards) counts as an interruption
                var interrupted = !clips.ContainsValue(clip) || clip.Weight <= 0f || clip.Time < fireTime;
                var elapsed = clip.Time - fireTime;

                if (interrupted)
                {
                    if (elapsed < soundEvent.DurationSeconds * soundEvent.DurationInterruptionThreshold)
                    {
                        handle.Stop();
                    }

                    activeClipSounds.RemoveAt(i);
                }
                else if (elapsed >= soundEvent.DurationSeconds)
                {
                    if (!soundEvent.ContinuePlayingSoundAtDurationEnd)
                    {
                        handle.Stop();
                    }

                    activeClipSounds.RemoveAt(i);
                }
            }
        }

        /// <summary>Pre-decodes every sound the animation's events can play. Call when the animation is loaded.</summary>
        public static void PrewarmAnimationSounds(AnimationBase animation)
        {
            PrewarmClipSounds(animation);
            PrewarmLegacyAnimationEventSounds(animation);
        }

        private static void PrewarmClipSounds(AnimationBase animation)
        {
            if (animation is not ClipAnimation clipAnimation)
            {
                return;
            }

            foreach (var clipEvent in clipAnimation.Events)
            {
                if (clipEvent is Animation.SoundEvent soundEvent)
                {
                    Sound.Cache(soundEvent.Name);
                }
            }
        }

        private static void PrewarmLegacyAnimationEventSounds(AnimationBase animation)
        {
            if (animation is not SequenceAnimation sequence)
            {
                return;
            }

            foreach (var sequenceEvent in sequence.Events)
            {
                if (sequenceEvent.Name is not ("AE_CL_PLAYSOUND" or "AE_CL_PLAYSOUND_ATTACHMENT"))
                {
                    continue;
                }

                var soundName = sequenceEvent.EventData?.GetStringProperty("name");

                if (!string.IsNullOrEmpty(soundName))
                {
                    Sound.Cache(soundName);
                }
            }
        }

        #endregion
    }
}
