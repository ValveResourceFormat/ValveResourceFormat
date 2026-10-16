using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the soundscape script "playrandom" operator (see <see cref="SoundscapeBank"/>): plays a
/// "rndwave" track at a volume and pitch drawn from an authored min/max range (the whole value, not an
/// offset), then reschedules on a random "time" interval while the soundscape is active. "position"
/// "random" places each play at a fresh random point around the listener; otherwise an authored "origin"
/// is a fixed spot, or it plays unspatialized.
/// </summary>
internal sealed class SoundEventScriptedRandom : SoundEvent
{
    private readonly string[] trackNames;
    private readonly (float Min, float Max) volumeRange;
    private readonly (float Min, float Max) pitchRange;
    private readonly (float Min, float Max) timeRange;
    private readonly bool randomPosition;
    private readonly Vector3? origin;
    private readonly float distanceMult;

    /// <summary>
    /// The first play waits half an interval, not a whole one, when a soundscape becomes active.
    /// </summary>
    private const float FirstIntervalScale = 0.5f;

    /// <summary>
    /// How far from the listener a "position" "random" sound is placed. Only the direction is randomized:
    /// scaling by the audible range puts it far enough out that the falloff swallows it.
    /// </summary>
    private const float RandomPositionRadius = 100f;

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
        distanceMult = SoundscapeOperatorParsing.SoundLevelToDistanceMult(data.GetStringProperty("soundlevel"));
    }

    protected override void DoStart()
    {
        if (WaitOutFirstInterval(FirstIntervalScale))
        {
            return;
        }

        if (trackNames.Length == 0)
        {
            return;
        }

        // Armed from this play starting, not from it finishing: counting the interval only once the track
        // has run out stretches every gap by the length of the sound, so the operator fires late and sparser
        // than it is authored to
        CheckRetrigger();

        Position = randomPosition ? PickRandomPosition() : origin;

        var pitch = Math.Clamp(float.Lerp(pitchRange.Min, pitchRange.Max, Random.NextSingle()) / 100f, 0.25f, 4f);
        var volume = MathUtils.Saturate(VolumeOverride ?? float.Lerp(volumeRange.Min, volumeRange.Max, Random.NextSingle())) * VolumeScale;

        if (StartTrack(trackNames, volume, pitch, range: 0f) is SampleProvider3D spatial)
        {
            spatial.DistanceMult = distanceMult;
        }
    }

    internal override void Prewarm(int depth) => PrewarmTracks(trackNames);

    /// <summary>
    /// Keeps the event in the mixer until the already armed next play comes around.
    /// </summary>
    private protected override bool StayAliveAfterFinishing() => RetriggerArmed;

    /// <summary>
    /// Picks a random direction around the listener, at a fixed distance. A real soundscape would pick
    /// between a handful of map-authored position markers instead - we don't have those wired through.
    /// </summary>
    private Vector3 PickRandomPosition()
    {
        var listener = Mixer.ListenerPosition;
        var angle = Random.NextSingle() * MathF.Tau;

        return listener + new Vector3(MathF.Cos(angle) * RandomPositionRadius, MathF.Sin(angle) * RandomPositionRadius, 0f);
    }
}
