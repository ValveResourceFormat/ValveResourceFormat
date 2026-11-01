using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "hlvr_phys_impact_basic" sound event type: one shot from one of up to four track lists, lightest
/// to heaviest, chosen by how hard the hit was. Not played: the rattle of a container's contents.
/// </summary>
internal sealed class SoundEventHLVRPhysImpact : SoundEvent
{
    private const float DefaultRange = 1500f;

    private static readonly string[] TierNames = ["a", "b", "c", "d"];

    private readonly (string[] Tracks, float Volume)[] tiers;
    private readonly float pitchRandMin;
    private readonly float pitchRandMax;
    private readonly string mixGroup;
    private readonly SoundEventCurve? distanceVolumeCurve;
    private readonly float range;

    public SoundEventHLVRPhysImpact(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;
        var graphVolume = data.ContainsKey("vgraph_volume_mult") ? data.GetSoundFloat("vgraph_volume_mult") : 1f;

        tiers = [.. TierNames
            .Select(tier => (Tracks: GetStringOrArrayProperty(data, $"vsnd_files_{tier}"),
                Volume: graphVolume * (data.ContainsKey($"vol_mult_{tier}") ? data.GetSoundFloat($"vol_mult_{tier}") : 1f)))
            .Where(static tier => tier.Tracks.Length > 0)];

        pitchRandMin = data.GetSoundFloat("pitch_rand_min");
        pitchRandMax = data.GetSoundFloat("pitch_rand_max");
        mixGroup = data.GetStringProperty("mixgroup", string.Empty);

        if (data.ContainsKey("volume_falloff_max"))
        {
            range = data.GetSoundFloat("volume_falloff_max");
            distanceVolumeCurve = SoundEventCurve.Linear(data.GetSoundFloat("volume_falloff_min"), 1f, range, 0f);
        }
        else
        {
            range = DefaultRange;
        }
    }

    protected override void DoStart()
    {
        if (tiers.Length == 0)
        {
            return;
        }

        // The caller scales volume by the square of the impact speed, the lists rise with the speed itself
        var strength = MathF.Sqrt(MathUtils.Saturate(VolumeScale));
        var (tracks, tierVolume) = tiers[Math.Min((int)(strength * tiers.Length), tiers.Length - 1)];

        StartTrack(tracks,
            GetRandomizedVolume(0f, 0f, mixGroup) * tierVolume,
            GetRandomizedPitch(pitchRandMin, pitchRandMax),
            range, distanceVolumeCurve);
    }

    internal override void Prewarm(int depth)
    {
        foreach (var (tracks, _) in tiers)
        {
            PrewarmTracks(tracks);
        }
    }
}
