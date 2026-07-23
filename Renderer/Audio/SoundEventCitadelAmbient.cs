using System.Diagnostics;
using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements"citadel_default_2d" and "citadel_ambient_3d" sound event types:
/// a single looping ambience track, decibel volume, and an optional fade-in on start.
/// </summary>
internal sealed class SoundEventCitadelAmbient : SoundEvent
{
    private readonly string[] trackNames;
    private readonly float volumeMult;
    private readonly float volumeFadeIn;
    private readonly string mixGroup;
    private readonly SoundEventCurve? distanceVolumeCurve;
    private readonly float range;

    private AudioSampleProvider? trackProvider;
    private float targetVolume;
    private float fadeInSecondsRemaining;
    private long startTimestamp;

    public SoundEventCitadelAmbient(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        trackNames = GetStringOrArrayProperty(data, "vsnd_files");
        volumeMult = data.GetFloatProperty("volume_mult", 1f);
        volumeFadeIn = data.GetFloatProperty("volume_fade_in");
        mixGroup = data.GetStringProperty("mixer_mixgroup", string.Empty);

        if (data.ContainsKey("spread_min") && data.ContainsKey("spread_max"))
        {
            distanceVolumeCurve = SoundEventCurve.Linear(
                data.GetFloatProperty("spread_min"), data.GetFloatProperty("spread_min_value", 1f),
                data.GetFloatProperty("spread_max"), data.GetFloatProperty("spread_max_value", 1f));
        }

        range = distanceVolumeCurve is { MaxX: > 0f } ? distanceVolumeCurve.MaxX : 512f;
    }

    protected override void DoStart()
    {
        if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        PositionOffset = Definition.PositionOffset;

        if (trackNames.Length == 0)
        {
            return;
        }

        var soundName = trackNames[Mixer.Player.PickTrack(Definition, trackNames.Length)];
        var cachedSound = Mixer.Player.SoundCache.GetSound(soundName);
        PlayingSoundFile = soundName;

        if (cachedSound == null)
        {
            return;
        }

        var position = Position.HasValue ? Position.Value + PositionOffset : (Vector3?)null;
        // 2 interleaved stereo samples per frame
        var delaySamples = (int)(Definition.Delay * SampleRate) * 2;

        trackProvider = BuildTrackProvider(cachedSound, position, Definition.Pitch, delaySamples);

        if (trackProvider is SampleProvider3D spatial)
        {
            spatial.Range = range;
            spatial.DistanceVolumeCurve = distanceVolumeCurve;
        }

        // Definition.Volume is in decibels for this event family
        var baseVolume = Math.Clamp(MathUtils.DecibelsToLinear(VolumeOverride ?? Definition.Volume), 0f, 1f);
        var mixGroupVolume = Mixer.Player.GetMixGroupVolume(mixGroup);
        targetVolume = baseVolume * volumeMult * mixGroupVolume;

        fadeInSecondsRemaining = volumeFadeIn;
        startTimestamp = Stopwatch.GetTimestamp();
        trackProvider.Volume = fadeInSecondsRemaining > 0f ? 0f : targetVolume;

        SampleProviders.Add(trackProvider);
    }

    /// <inheritdoc/>
    public override bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        if (trackProvider != null && fadeInSecondsRemaining > 0f)
        {
            var elapsed = (float)Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;

            if (elapsed >= fadeInSecondsRemaining)
            {
                trackProvider.Volume = targetVolume;
                fadeInSecondsRemaining = 0f;
            }
            else
            {
                trackProvider.Volume = targetVolume * (elapsed / fadeInSecondsRemaining);
            }
        }

        return base.Update(listenerPosition, rightEarDirection);
    }
}
