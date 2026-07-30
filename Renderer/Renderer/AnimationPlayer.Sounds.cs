using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Plays the sounds of the events the player samples.
    /// </summary>
    public partial class AnimationPlayer
    {
        /// <summary>Gets or sets whether sampled events play their sounds. Event sampling itself is unaffected.</summary>
        public bool SkipEvents
        {
            get => field;
            set
            {
                var wasSkipping = field;
                field = value;

                if (wasSkipping && !value)
                {
                    foreach (var clip in clips.Values)
                    {
                        PreCacheAnimationSounds(clip.Animation);
                    }
                }
            }
        }

        /// <summary>Gets or sets the resolver from an attachment name to a world position, an empty name being the model itself.</summary>
        public Func<string, Vector3?>? ResolvePosition { get; set; }

        private readonly record struct ActiveClipSound(
            Audio.SoundEvent Handle,
            PlaybackClip Clip,
            NmSoundEvent Event,
            float FireTime);

        private readonly List<ActiveClipSound> activeClipSounds = [];

        private void PlayEventSound(PlaybackClip clip, NmClipEvent clipEvent, float newTime, bool finished)
        {
            if (SkipEvents || clipEvent is not NmSoundEvent soundEvent || soundEvent.Relevance == "ServerOnly")
            {
                return;
            }

            var duration = clip.Animation.Duration;
            var currentTime = duration > 0f ? newTime % duration : newTime;

            var fireTime = finished && clipEvent.StartTime >= currentTime
                ? newTime
                : newTime - ((currentTime - clipEvent.StartTime + duration) % duration);

            // "EntityEyePos" is the listener itself, play it unspatialized; "EntityPos" plays at the entity
            var position = soundEvent.Position == "EntityPos"
                ? ResolvePosition?.Invoke(soundEvent.AttachmentName)
                : null;

            var handle = Sound.Play(soundEvent.Name, position);

            if (handle != null && soundEvent.Duration > 0f)
            {
                activeClipSounds.Add(new ActiveClipSound(handle, clip, soundEvent, fireTime));
            }
        }

        private void PlayEventSound(AnimationEvent sequenceEvent)
        {
            var isAttachment = sequenceEvent.Name == "AE_CL_PLAYSOUND_ATTACHMENT";

            if (SkipEvents || (!isAttachment && sequenceEvent.Name != "AE_CL_PLAYSOUND"))
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
                    if (elapsed < soundEvent.Duration * soundEvent.DurationInterruptionThreshold)
                    {
                        handle.Stop();
                    }

                    activeClipSounds.RemoveAt(i);
                }
                else if (elapsed >= soundEvent.Duration)
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
        public void PreCacheAnimationSounds(Animation animation)
        {
            if (SkipEvents)
            {
                return;
            }

            PreCacheClipSounds(animation);
            PreCacheLegacyAnimationEventSounds(animation);
        }

        private static void PreCacheClipSounds(Animation animation)
        {
            if (animation is not ClipAnimation clipAnimation)
            {
                return;
            }

            foreach (var clipEvent in clipAnimation.Events)
            {
                if (clipEvent is NmSoundEvent soundEvent)
                {
                    Sound.Cache(soundEvent.Name);
                }
            }
        }

        private static void PreCacheLegacyAnimationEventSounds(Animation animation)
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
    }
}
