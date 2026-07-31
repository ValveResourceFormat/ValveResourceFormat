using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements a classic soundscape script's "playlooping" operator (see <see cref="SoundscapeBank"/>):
/// a single track (or, less commonly, a "rndwave" picked once at start) at a flat authored volume/pitch,
/// looping via the vsnd's own baked-in loop points like the rest of the audio system.
/// An authored "origin" (a literal world position, unlike the modern vsndevt schema's "position" array)
/// spatializes it with a range derived from "soundlevel"; without one it plays unspatialized (e.g. a
/// weather bed).
/// </summary>
internal sealed class SoundEventScriptedLoop : SoundEvent
{
    private readonly string[] trackNames;
    private readonly float volume;
    private readonly float pitch;
    private readonly Vector3? origin;
    private readonly float range;

    public SoundEventScriptedLoop(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data.GetSubCollection("operator");

        var singleWave = data.GetStringProperty("wave");
        trackNames = !string.IsNullOrEmpty(singleWave) ? [singleWave] : SoundscapeOperatorParsing.GetRandomWaveFiles(data);

        volume = data.GetFloatProperty("volume", 1f);
        pitch = data.GetFloatProperty("pitch", 100f) / 100f;
        origin = SoundscapeOperatorParsing.ParseOrigin(data);
        range = SoundscapeOperatorParsing.SoundLevelToRange(data.GetStringProperty("soundlevel"), 1000f);
    }

    protected override void DoStart()
    {
        if (trackNames.Length == 0)
        {
            return;
        }

        Position = origin;

        var trackVolume = Math.Clamp(VolumeOverride ?? volume, 0f, 1f);

        StartTrack(trackNames, trackVolume, Math.Clamp(pitch, 0.25f, 4f), range);
    }

    internal override void Prewarm(int depth) => PrewarmTracks(trackNames);

    protected override void OnFinished()
    {
        base.OnFinished();

        // A genuinely looping track (baked-in loop points) never reaches here; this only catches a
        // mistakenly non-looping vsnd, so it doesn't stay registered producing nothing forever.
        if (!FadingOut)
        {
            Stop();
        }
    }
}
