using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "hlvr_ambient_fixed_rotation" sound event type: one looping track placed at a fixed
/// bearing around the emitter ("rotation_angle"/"vertical_rotation_angle" at "radius"), with a linear
/// distance falloff and authored fade in/out. Maps are authored with one event per corner (the _L/_R/_LS/_RS
/// naming, at 45/135/225/315 degrees), which together surround the listener with the ambient bed.
/// </summary>
/// <remarks>
/// The "opvar"-driven volume remapping ("global_opvar_name_01", "remap_vol_opvar_*"), HRTF ("use_hrtf",
/// "hrtf_mix"), named sends and the "spread_min"/"spread_max" source width are not modeled, matching every
/// other event type here.
/// </remarks>
internal sealed class SoundEventHLVRAmbientFixedRotation : SoundEvent
{
    private readonly string[] trackNames;
    private readonly string mixGroup;
    private readonly SoundEventCurve? distanceVolumeCurve;
    private readonly float range;
    private readonly Vector3 rotationOffset;

    public SoundEventHLVRAmbientFixedRotation(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        trackNames = GetStringOrArrayProperty(data, "vsnd_files");
        mixGroup = data.GetStringProperty("mixgroup", string.Empty);

        var radius = data.GetFloatProperty("radius");
        var yaw = data.GetFloatProperty("rotation_angle");
        var pitch = data.GetFloatProperty("vertical_rotation_angle");

        rotationOffset = radius * EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(pitch, yaw, 0f));

        if (data.ContainsKey("volume_falloff_max"))
        {
            distanceVolumeCurve = SoundEventCurve.Linear(
                data.GetFloatProperty("volume_falloff_min"), 1f,
                data.GetFloatProperty("volume_falloff_max"), 0f);
        }

        range = distanceVolumeCurve is { MaxX: > 0f } ? distanceVolumeCurve.MaxX
            : radius > 0f ? radius
            : 1000f;
    }

    protected override void DoStart()
    {
        // On top of the definition's own offset the base already applied
        PositionOffset += rotationOffset;

        var volume = Math.Clamp(VolumeOverride ?? Definition.Volume, 0f, 1f) * VolumeScale * Mixer.Player.GetMixGroupVolume(mixGroup);

        StartTrack(trackNames, volume, Definition.Pitch, range, distanceVolumeCurve);

        if (Definition.FadeIn > 0f)
        {
            FadeIn(Definition.FadeIn);
        }
    }

    internal override void Prewarm(int depth) => PrewarmTracks(trackNames);
}
