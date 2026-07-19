using System.Globalization;
using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "csgo_mega" sound event type: a random sound picked from a track list,
/// optional child sound events, and optional periodic retriggering.
/// </summary>
internal sealed class SoundEventCSGOMega : SoundEvent
{
    private bool wasInitialized;
    private bool waitingForRetrigger;
    private DateTime retriggerTime = DateTime.MinValue;

    public SoundEventCSGOMega(KVObject soundEvent) : base(soundEvent)
    {
    }

    protected override void DoStart()
    {
        if (SoundEventData.ContainsKey("position"))
        {
            Position = new Vector3(SoundEventData.GetFloatArray("position"));
        }

        if (SoundEventData.ContainsKey("position_offset"))
        {
            PositionOffset = new Vector3(SoundEventData.GetFloatArray("position_offset"));
        }

        if (!wasInitialized && CheckRetrigger())
        {
            // Retriggered events wait out their first interval before playing
            wasInitialized = true;
            return;
        }

        wasInitialized = true;

        var soundNames = GetStringArrayProperty("vsnd_files_track_01");
        if (soundNames.Length > 0)
        {
            var soundName = soundNames[Mixer.Player.PickTrack(SoundEventData, soundNames.Length)];
            var cachedSound = Mixer.Player.Cache.GetSound(soundName);

            if (cachedSound != null)
            {
                var source = new CachedSoundSampleProvider(cachedSound)
                {
                    Pitch = GetRandomizedPitch(),
                    // 2 interleaved stereo samples per frame
                    DelaySamples = (int)(SoundEventData.GetFloatProperty("delay") * SampleRate) * 2,
                };

                AudioSampleProvider sampleProvider;

                if (Position.HasValue)
                {
                    var volumeCurve = SoundEventData.GetBooleanProperty("use_distance_volume_mapping_curve")
                        ? SoundEventCurve.Parse(SoundEventData, "distance_volume_mapping_curve")
                        : null;
                    var stereoCurve = SoundEventData.GetBooleanProperty("use_distance_unfiltered_stereo_mapping_curve")
                        ? SoundEventCurve.Parse(SoundEventData, "distance_unfiltered_stereo_mapping_curve")
                        : null;

                    sampleProvider = new SampleProvider3D(source)
                    {
                        Position = Position.Value + PositionOffset,
                        Range = volumeCurve?.MaxX ?? GetRange(),
                        DistanceVolumeCurve = volumeCurve,
                        StereoMixCurve = stereoCurve,
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

        if (!SoundEventData.GetBooleanProperty("enable_child_events"))
        {
            return;
        }

        foreach (var childName in GetStringArrayProperty("soundevent_01"))
        {
            var childData = Mixer.Player.Bank.GetSoundEvent(childName);
            if (childData == null)
            {
                continue;
            }

            var childSoundEvent = Build(childData);
            if (childSoundEvent != null)
            {
                StartAsChild(childSoundEvent);
            }
        }
    }

    protected override void OnFinished()
    {
        base.OnFinished();
        CheckRetrigger();
    }

    private bool CheckRetrigger()
    {
        if (!SoundEventData.GetBooleanProperty("enable_retrigger"))
        {
            return false;
        }

        var retriggerMin = SoundEventData.GetFloatProperty("retrigger_interval_min");
        var retriggerMax = SoundEventData.GetFloatProperty("retrigger_interval_max");
        var retriggerAt = float.Lerp(retriggerMin, retriggerMax, Random.NextSingle());
        retriggerTime = DateTime.UtcNow.AddSeconds(retriggerAt);
        waitingForRetrigger = true;
        return true;
    }

    public override bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        if (Started && waitingForRetrigger && DateTime.UtcNow >= retriggerTime)
        {
            waitingForRetrigger = false;
            Start();
        }

        return base.Update(listenerPosition, rightEarDirection);
    }

    private float GetRandomizedPitch()
    {
        var pitch = SoundEventData.GetFloatProperty("pitch", 1f);
        var randomMin = SoundEventData.GetFloatProperty("pitch_random_min");
        var randomMax = SoundEventData.GetFloatProperty("pitch_random_max");

        if (randomMin != 0f || randomMax != 0f)
        {
            pitch += float.Lerp(randomMin, randomMax, Random.NextSingle());
        }

        return Math.Clamp(pitch, 0.25f, 4f);
    }

    private float GetRandomizedVolume()
    {
        // Events like Gear.JumpLand.CT have volume 0.0 in their data: the game passes the volume at play time
        var volume = VolumeOverride ?? SoundEventData.GetFloatProperty("volume", 1f);
        var randomMin = SoundEventData.GetFloatProperty("volume_random_min");
        var randomMax = SoundEventData.GetFloatProperty("volume_random_max");

        if (randomMin != 0f || randomMax != 0f)
        {
            volume += float.Lerp(randomMin, randomMax, Random.NextSingle());
        }

        var mixGroupVolume = Mixer.Player.GetMixGroupVolume(SoundEventData.GetStringProperty("mixgroup", string.Empty));

        return Math.Clamp(volume, 0f, 1f) * mixGroupVolume;
    }

    private float GetRange()
    {
        var range = 0f;

        if (SoundEventData.TryGetValue("distance_volume_mapping_curve", out var curveValue) && curveValue.ValueType == KVValueType.Array)
        {
            // Curve points are arrays of [distance, volume, ...], the largest distance is the audible range
            foreach (var point in SoundEventData.GetArray("distance_volume_mapping_curve"))
            {
                range = Math.Max(range, Convert.ToSingle(point[0], CultureInfo.InvariantCulture));
            }
        }

        return range > 0f ? range : 1000f;
    }

    private string[] GetStringArrayProperty(string name)
    {
        if (!SoundEventData.TryGetValue(name, out var value))
        {
            return [];
        }

        if (value.ValueType == KVValueType.Array)
        {
            return SoundEventData.GetArray<string>(name);
        }

        var single = SoundEventData.GetStringProperty(name);
        return single != null ? [single] : [];
    }
}
