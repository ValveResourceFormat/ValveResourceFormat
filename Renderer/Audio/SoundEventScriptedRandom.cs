using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements a classic soundscape script's "playrandom" operator (see <see cref="SoundscapeBank"/>):
/// picks a track from "rndwave", plays it once at a volume/pitch drawn from an authored min/max range,
/// then reschedules itself on a random "time" interval for as long as the soundscape stays active.
/// Unlike the modern event types the range here is the whole value, not an offset added to a base.
/// "position" "random" places the sound at a fresh random point around the listener on every retrigger;
/// otherwise an authored "origin" (a literal world position) is used as a fixed spot, or the sound plays
/// unspatialized when neither is present.
/// </summary>
internal sealed class SoundEventScriptedRandom : SoundEvent
{
    private readonly string[] trackNames;
    private readonly (float Min, float Max) volumeRange;
    private readonly (float Min, float Max) pitchRange;
    private readonly (float Min, float Max) timeRange;
    private readonly bool randomPosition;
    private readonly Vector3? origin;
    private readonly float range;

    private protected override (float Min, float Max)? RetriggerInterval => timeRange;

    public SoundEventScriptedRandom(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data.GetSubCollection("operator");

        trackNames = SoundscapeOperatorParsing.GetRandomWaveFiles(data);
        volumeRange = SoundscapeOperatorParsing.ParseRange(data, "volume", 1f);
        pitchRange = SoundscapeOperatorParsing.ParseRange(data, "pitch", 100f);
        timeRange = SoundscapeOperatorParsing.ParseRange(data, "time", 10f);
        randomPosition = string.Equals(data.GetStringProperty("position"), "random", StringComparison.OrdinalIgnoreCase);
        origin = SoundscapeOperatorParsing.ParseOrigin(data);
        range = SoundscapeOperatorParsing.SoundLevelToRange(data.GetStringProperty("soundlevel"), 1000f);
    }

    protected override void DoStart()
    {
        if (WaitOutFirstInterval())
        {
            return;
        }

        if (trackNames.Length == 0)
        {
            return;
        }

        Position = randomPosition ? PickRandomPosition() : origin;

        var pitch = Math.Clamp(float.Lerp(pitchRange.Min, pitchRange.Max, Random.NextSingle()) / 100f, 0.25f, 4f);
        var volume = Math.Clamp(VolumeOverride ?? float.Lerp(volumeRange.Min, volumeRange.Max, Random.NextSingle()), 0f, 1f);

        StartTrack(trackNames, volume, pitch, range);
    }

    internal override void Prewarm(int depth) => PrewarmTracks(trackNames);

    private protected override bool StayAliveAfterFinishing() => CheckRetrigger();

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
