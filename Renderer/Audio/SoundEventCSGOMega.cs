using System.Diagnostics;
using ValveResourceFormat.Renderer.Audio.SampleProviders;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "csgo_mega" sound event type: a random sound picked from a track list,
/// optional child sound events, and optional periodic retriggering.
/// </summary>
internal sealed class SoundEventCSGOMega : SoundEvent
{
    private bool wasInitialized;
    private bool waitingForRetrigger;
    private long retriggerTimestamp;

    public SoundEventCSGOMega(SoundEventDefinition definition) : base(definition)
    {
    }

    protected override void DoStart()
    {
        // A position supplied by the caller (e.g. a point_soundevent entity) wins over the definition's
        // "position" key (all-zero placeholders are already dropped at parse time).
        if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        PositionOffset = Definition.PositionOffset;

        if (!wasInitialized && CheckRetrigger())
        {
            // Retriggered events wait out their first interval before playing
            wasInitialized = true;
            return;
        }

        wasInitialized = true;

        var soundNames = Definition.TrackNames;
        if (soundNames.Length > 0)
        {
            var soundName = soundNames[Mixer.Player.PickTrack(Definition)];
            var cachedSound = Mixer.Player.SoundCache.GetSound(soundName);
            PlayingSoundFile = soundName;

            if (cachedSound != null)
            {
                var source = new CachedSoundSampleProvider(cachedSound)
                {
                    Pitch = GetRandomizedPitch(),
                    // 2 interleaved stereo samples per frame
                    DelaySamples = (int)(Definition.Delay * SampleRate) * 2,
                };

                AudioSampleProvider sampleProvider;

                if (Position.HasValue)
                {
                    sampleProvider = new SampleProvider3D(source)
                    {
                        Position = Position.Value + PositionOffset,
                        Range = Definition.Range,
                        DistanceVolumeCurve = Definition.DistanceVolumeCurve,
                        StereoMixCurve = Definition.StereoMixCurve,
                        Volume = GetRandomizedVolume(),
                    };
                }
                else
                {
                    sampleProvider = new SampleProvider2D(source)
                    {
                        Volume = GetRandomizedVolume(),
                    };
                }

                SampleProviders.Add(sampleProvider);
            }
        }

        var childNames = Definition.ChildEventNames;
        if (childNames.Length == 0)
        {
            return;
        }

        // Child definitions are resolved through the bank once and kept on the parent definition
        var childDefinitions = Definition.ChildDefinitions;
        if (childDefinitions == null)
        {
            childDefinitions = new SoundEventDefinition?[childNames.Length];

            for (var i = 0; i < childNames.Length; i++)
            {
                childDefinitions[i] = Mixer.Player.Bank.GetSoundEvent(childNames[i]);
            }

            Definition.ChildDefinitions = childDefinitions;
        }

        foreach (var childDefinition in childDefinitions)
        {
            if (childDefinition == null)
            {
                continue;
            }

            var childSoundEvent = Build(childDefinition);
            if (childSoundEvent != null)
            {
                StartAsChild(childSoundEvent);
            }
        }
    }

    protected override void OnFinished()
    {
        base.OnFinished();

        if (FadingOut)
        {
            // The base already completed the stop
            return;
        }

        if (CheckRetrigger())
        {
            return;
        }

        // One-shot: fully stop once nothing in the tree can produce samples anymore, so the event
        // leaves the mixer's active set instead of staying registered (and updated) forever.
        // A child that is still started may be waiting on its own retrigger and keeps us alive.
        if (!AnyChildStarted())
        {
            Stop();
        }
    }

    private bool CheckRetrigger()
    {
        if (!Definition.EnableRetrigger)
        {
            return false;
        }

        var retriggerAt = float.Lerp(Definition.RetriggerIntervalMin, Definition.RetriggerIntervalMax, Random.NextSingle());
        retriggerTimestamp = Stopwatch.GetTimestamp() + (long)(retriggerAt * Stopwatch.Frequency);
        waitingForRetrigger = true;
        return true;
    }

    public override bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        if (Started && !FadingOut && waitingForRetrigger && Stopwatch.GetTimestamp() >= retriggerTimestamp)
        {
            waitingForRetrigger = false;
            Start();
        }

        return base.Update(listenerPosition, rightEarDirection);
    }

    private float GetRandomizedPitch()
    {
        var pitch = Definition.Pitch;

        if (Definition.PitchRandomMin != 0f || Definition.PitchRandomMax != 0f)
        {
            pitch += float.Lerp(Definition.PitchRandomMin, Definition.PitchRandomMax, Random.NextSingle());
        }

        return Math.Clamp(pitch, 0.25f, 4f);
    }

    private float GetRandomizedVolume()
    {
        // Events like Gear.JumpLand.CT have volume 0.0 in their data: the game passes the volume at play time
        var volume = VolumeOverride ?? Definition.Volume;

        if (Definition.VolumeRandomMin != 0f || Definition.VolumeRandomMax != 0f)
        {
            volume += float.Lerp(Definition.VolumeRandomMin, Definition.VolumeRandomMax, Random.NextSingle());
        }

        var mixGroupVolume = Mixer.Player.GetMixGroupVolume(Definition.MixGroup);

        return Math.Clamp(volume, 0f, 1f) * mixGroupVolume;
    }
}
