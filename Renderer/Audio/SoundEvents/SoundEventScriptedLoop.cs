using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements a classic soundscape script's "playlooping" operator (see <see cref="SoundscapeBank"/>):
/// a single track (or, less commonly, a "rndwave" picked once at start) at a flat authored volume/pitch,
/// looping via the vsnd's own baked-in loop points like the rest of the audio system.
/// An authored "origin" (a literal world position, unlike the modern vsndevt schema's "position" array)
/// spatializes it, falling off by the distance law its "soundlevel" implies; without one it plays
/// unspatialized (e.g. a weather bed).
/// </summary>
internal sealed class SoundEventScriptedLoop : SoundEvent
{
    private readonly string[] trackNames;
    private readonly float volume;
    private readonly float pitch;
    private readonly Vector3? origin;
    private readonly float distanceMult;

    public SoundEventScriptedLoop(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data.GetSubCollection("operator");

        var singleWave = data.GetStringProperty("wave");
        trackNames = !string.IsNullOrEmpty(singleWave) ? [singleWave] : SoundscapeOperatorParsing.GetRandomWaveFiles(data);

        volume = data.GetFloatProperty("volume", 1f);
        pitch = data.GetFloatProperty("pitch", 100f) / 100f;
        origin = SoundscapeOperatorParsing.ParseOrigin(data);
        distanceMult = SoundscapeOperatorParsing.SoundLevelToDistanceMult(data.GetStringProperty("soundlevel"));
    }

    protected override void DoStart()
    {
        if (trackNames.Length == 0)
        {
            return;
        }

        Position = origin;

        var trackVolume = Math.Clamp(VolumeOverride ?? volume, 0f, 1f) * VolumeScale;

        if (StartTrack(trackNames, trackVolume, Math.Clamp(pitch, 0.25f, 4f), range: 0f) is SampleProvider3D spatial)
        {
            spatial.DistanceMult = distanceMult;
        }
    }

    internal override void Prewarm(int depth) => PrewarmTracks(trackNames);
}
