using System.Diagnostics;
using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements a classic soundscape script's "playrandom" operator (see <see cref="SoundscapeBank"/>):
/// picks a track from "rndwave", plays it once at a volume/pitch drawn from an authored min/max range,
/// then reschedules itself on a random "time" interval for as long as the soundscape stays active.
/// Unlike the modern event types the range here is the whole value, not an offset added to a base, and
/// "position" "random" places the sound at a fresh random point around the listener on every retrigger
/// instead of at an authored or caller-supplied position.
/// </summary>
internal sealed class SoundEventScriptedRandom : SoundEvent
{
    private readonly string[] trackNames;
    private readonly (float Min, float Max) volumeRange;
    private readonly (float Min, float Max) pitchRange;
    private readonly (float Min, float Max) timeRange;
    private readonly bool randomPosition;
    private readonly float range;

    private bool wasInitialized;
    private bool waitingForRetrigger;
    private long retriggerTimestamp;

    private protected override bool WaitingToStart => waitingForRetrigger;

    public SoundEventScriptedRandom(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data.GetSubCollection("operator");

        trackNames = SoundscapeOperatorParsing.GetRandomWaveFiles(data);
        volumeRange = SoundscapeOperatorParsing.ParseRange(data, "volume", 1f);
        pitchRange = SoundscapeOperatorParsing.ParseRange(data, "pitch", 100f);
        timeRange = SoundscapeOperatorParsing.ParseRange(data, "time", 10f);
        randomPosition = string.Equals(data.GetStringProperty("position"), "random", StringComparison.OrdinalIgnoreCase);
        range = SoundscapeOperatorParsing.SoundLevelToRange(data.GetStringProperty("soundlevel"), 1000f);
    }

    protected override void DoStart()
    {
        if (!wasInitialized && CheckRetrigger())
        {
            // Waits out its first interval before playing, same as entering a modern retriggered event's area
            wasInitialized = true;
            return;
        }

        wasInitialized = true;

        if (trackNames.Length == 0)
        {
            return;
        }

        Position = randomPosition ? PickRandomPosition() : null;

        var soundName = trackNames[Mixer.Player.PickTrack(Definition, trackNames.Length)];
        var cachedSound = Mixer.Player.SoundCache.GetSound(soundName);
        PlayingSoundFile = soundName;

        if (cachedSound == null)
        {
            return;
        }

        // 2 interleaved stereo samples per frame
        var delaySamples = (int)(Definition.Delay * SampleRate) * 2;
        var pitch = Math.Clamp(float.Lerp(pitchRange.Min, pitchRange.Max, Random.NextSingle()) / 100f, 0.25f, 4f);

        var sampleProvider = BuildTrackProvider(cachedSound, Position, pitch, delaySamples);
        sampleProvider.Volume = Math.Clamp(VolumeOverride ?? float.Lerp(volumeRange.Min, volumeRange.Max, Random.NextSingle()), 0f, 1f);

        if (sampleProvider is SampleProvider3D spatial)
        {
            spatial.Range = range;
        }

        SampleProviders.Add(sampleProvider);
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

        // Track list came back empty (e.g. every vsnd failed to decode): nothing left to retry with, so
        // leave the mixer's active set instead of retriggering into silence forever.
        Stop();
    }

    private bool CheckRetrigger()
    {
        var retriggerAt = float.Lerp(timeRange.Min, timeRange.Max, Random.NextSingle());
        retriggerTimestamp = Stopwatch.GetTimestamp() + (long)(retriggerAt * Stopwatch.Frequency);
        waitingForRetrigger = true;
        return true;
    }

    /// <inheritdoc/>
    public override bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        if (Started && !FadingOut && waitingForRetrigger && Stopwatch.GetTimestamp() >= retriggerTimestamp)
        {
            waitingForRetrigger = false;
            Start();
        }

        return base.Update(listenerPosition, rightEarDirection);
    }

    /// <summary>
    /// Picks a random point on a ring around the listener. A real soundscape would pick between a
    /// handful of map-authored position markers instead - we don't have those wired through, so this
    /// spreads retriggers across random directions/distances within the operator's audible range.
    /// </summary>
    private Vector3 PickRandomPosition()
    {
        var listener = Mixer.ListenerPosition;
        var angle = Random.NextSingle() * MathF.Tau;
        var distance = float.Lerp(range * 0.25f, range * 0.9f, Random.NextSingle());

        return listener + new Vector3(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance, 0f);
    }
}
