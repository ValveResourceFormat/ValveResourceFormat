using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Plays the sounds of the events the player samples: clip sound events (CNmSoundEvent) and the
    /// "AE_CL_PLAYSOUND" family on sequence animations.
    /// </summary>
    public partial class AnimationPlayer
    {
        /// <summary>
        /// Gets or sets whether sampled events play their sounds. Sampling itself is unaffected, so
        /// <see cref="ClipEventFired"/> and <see cref="SequenceEventFired"/> keep firing either way.
        /// Clearing this pre-caches the sound events of the clips already loaded.
        /// </summary>
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

        /// <summary>
        /// Optional resolver from an attachment/bone name to a world position, used to place the sounds an
        /// event plays at the model (an empty name means the model's own position). The owning scene node
        /// holds the world transform, so it wires this up. Sounds play unspatialized when unset.
        /// </summary>
        public Func<string, Vector3?>? ResolvePosition { get; set; }

        /// <summary>
        /// A clip sound with a duration window that may need to be cut short: either at the end of its
        /// event window (m_bContinuePlayingSoundAtDurationEnd) or when the animation is interrupted
        /// before the interruption threshold (m_flDurationInterruptionThreshold).
        /// </summary>
        private readonly record struct ActiveClipSound(
            Audio.SoundEvent Handle,
            PlaybackClip Clip,
            NmSoundEvent Event,
            float FireTime);

        private readonly List<ActiveClipSound> activeClipSounds = [];

        /// <summary>
        /// Plays the sound of a clip event, if it is one that plays a sound.
        /// </summary>
        /// <param name="clip">The clip that fired the event.</param>
        /// <param name="clipEvent">The event that fired.</param>
        /// <param name="newTime">The clip's playback time in seconds after the update that fired it.</param>
        /// <param name="finished">Whether this was the last update of a non looping clip.</param>
        private void PlayEventSound(PlaybackClip clip, NmClipEvent clipEvent, float newTime, bool finished)
        {
            if (SkipEvents || clipEvent is not NmSoundEvent soundEvent || soundEvent.Relevance == "ServerOnly")
            {
                return;
            }

            // The event fired somewhere inside (previousTime, newTime]: reconstruct its actual time on the
            // clip's unwrapped timeline so duration windows measure from the event itself, not from the end
            // of the frame that crossed it
            var duration = clip.Animation.Duration;
            var currentTime = duration > 0f ? newTime % duration : newTime;

            var fireTime = finished && clipEvent.StartTime >= currentTime
                // An end-of-clip event fires at the moment the clip finishes; the wrap formula
                // below would place it a whole loop in the past
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

        /// <summary>
        /// Plays the sound of an "AE_CL_PLAYSOUND"/"AE_CL_PLAYSOUND_ATTACHMENT" sequence animation event.
        /// The sound event name is the "name" key of the event's m_EventData sub-collection, not m_sOptions
        /// (which is unused for this event type). The "_ATTACHMENT" variant additionally carries an
        /// "attachment" key naming where to play the sound; plain "AE_CL_PLAYSOUND" plays at the model's own
        /// position. Both go through <see cref="ResolvePosition"/> (an empty name means the model itself).
        /// Unlike <see cref="NmSoundEvent"/>, these have no duration window - they are fire-and-forget.
        /// </summary>
        private void PlayEventSound(AnimationEvent sequenceEvent)
        {
            var isAttachment = sequenceEvent.Name == "AE_CL_PLAYSOUND_ATTACHMENT";

            if (SkipEvents || (!isAttachment && sequenceEvent.Name != "AE_CL_PLAYSOUND"))
            {
                return;
            }

            var soundName = sequenceEvent.EventData?.GetStringProperty("name");

            if (string.IsNullOrEmpty(soundName))
            {
                return;
            }

            var attachmentName = isAttachment ? sequenceEvent.EventData!.GetStringProperty("attachment") : string.Empty;

            Sound.Play(soundName, ResolvePosition?.Invoke(attachmentName));
        }

        /// <summary>
        /// Enforces the duration windows of playing clip sounds: cuts sounds at the end of their window unless
        /// they are flagged to continue, and cuts sounds whose animation was interrupted before the threshold.
        /// </summary>
        private void UpdateActiveClipSounds()
        {
            for (var i = activeClipSounds.Count - 1; i >= 0; i--)
            {
                var (handle, clip, soundEvent, fireTime) = activeClipSounds[i];

                if (!handle.Started)
                {
                    // The sound already finished on its own
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

        /// <summary>
        /// Pre-decodes (and pre-builds, see <c>SoundEventPlayer.Cache</c>) every sound event the animation
        /// can fire - clip events (CNmSoundEvent) and legacy "AE_CL_PLAYSOUND" events alike - so the first
        /// playback does no decode or build work mid-frame. Call when the animation is loaded, not when it
        /// first plays. No-op when no sound player is active or <see cref="SkipEvents"/> is set.
        /// </summary>
        public void PreCacheAnimationSounds(Animation animation)
        {
            if (SkipEvents)
            {
                return;
            }

            PreCacheClipSounds(animation);
            PreCacheLegacyAnimationEventSounds(animation);
        }

        /// <summary>
        /// Pre-decodes every sound event a clip can fire. No-op when no sound player is active.
        /// </summary>
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

        /// <summary>
        /// Pre-decodes every sound an "AE_CL_PLAYSOUND"/"AE_CL_PLAYSOUND_ATTACHMENT" sequence animation event
        /// can fire. No-op when no sound player is active.
        /// </summary>
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
