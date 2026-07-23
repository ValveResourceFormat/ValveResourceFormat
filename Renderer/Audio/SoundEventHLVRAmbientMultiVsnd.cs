using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "hlvr_ambient_fixed_rotation_multi_vsnd" sound event type: up to 8 looping tracks
/// ("vsnd_file_01".."_08"), each mixed in simultaneously at its own fixed weight ("vsnd_vol_01".."_08"),
/// with an overall volume and linear distance falloff.
/// </summary>
/// <remarks>
/// The "fixed_rotation" in the type name and its "rotation_angle"/"vertical_rotation_angle" fields imply
/// the authored intent is a directional blend (these look like takes recorded facing different compass
/// directions, cross-faded by the listener's orientation) - not modeled here, all non-zero-weighted
/// layers just play together as a static mix. The "opvar"-driven volume/filter remapping
/// ("global_opvar_name_01", "remap_vol_opvar_*", "remap_filter_freq_opvar_*", "filter_mix") ties into the
/// game's live parameter system and isn't modeled either, matching every other event type here.
/// Each layer is wrapped as an anonymous "hlvr_default_3d" child (see <see cref="SoundEventHLVRDefault"/>)
/// rather than given its own sample-provider plumbing, reusing its distance falloff and looping (via the
/// vsnd's own baked-in loop points) unchanged.
/// </remarks>
internal sealed class SoundEventHLVRAmbientMultiVsnd : SoundEvent
{
    private const int MaxLayers = 8;

    private readonly (string File, float Weight)[] layers;
    private readonly string mixGroup;
    private readonly float falloffMin;
    private readonly float falloffMax;

    public SoundEventHLVRAmbientMultiVsnd(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;
        var list = new List<(string, float)>();

        for (var i = 1; i <= MaxLayers; i++)
        {
            var suffix = i.ToString("D2", CultureInfo.InvariantCulture);
            var file = data.GetStringProperty("vsnd_file_" + suffix);
            var weight = data.GetFloatProperty("vsnd_vol_" + suffix);

            if (!string.IsNullOrEmpty(file) && weight > 0f)
            {
                list.Add((file, weight));
            }
        }

        layers = [.. list];
        mixGroup = data.GetStringProperty("mixgroup", string.Empty);
        falloffMin = data.GetFloatProperty("volume_falloff_min");
        falloffMax = data.ContainsKey("volume_falloff_max") ? data.GetFloatProperty("volume_falloff_max") : data.GetFloatProperty("radius", 1000f);
    }

    protected override void DoStart()
    {
        if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        PositionOffset = Definition.PositionOffset;

        if (layers.Length == 0)
        {
            return;
        }

        var baseVolume = Math.Clamp(VolumeOverride ?? Definition.Volume, 0f, 1f) * Mixer.Player.GetMixGroupVolume(mixGroup);
        var delay = DelayOverride ?? Definition.Delay;

        var childDefinitions = Definition.ChildDefinitions ??= BuildLayerDefinitions(delay);
        StartChildren(childDefinitions, (child, i) =>
        {
            child.Position = Position;
            child.VolumeOverride = baseVolume * layers[i].Weight;
        });
    }

    /// <summary>
    /// Wraps each layer as an anonymous "hlvr_default_3d" child. The resolved start delay (own
    /// <see cref="SoundEvent.DelayOverride"/>, e.g. a quad channel's phase stagger, falling back to this
    /// definition's own authored delay) is baked directly into each synthetic child's "delay" property
    /// rather than threaded through as a per-instance override: this method only ever runs once per
    /// instance (cached on <see cref="SoundEventDefinition.ChildDefinitions"/>), matching every other
    /// child-definition-resolving type here.
    /// </summary>
    private SoundEventDefinition[] BuildLayerDefinitions(float delay)
    {
        var definitions = new SoundEventDefinition[layers.Length];

        for (var i = 0; i < layers.Length; i++)
        {
            var wrapper = new KVObject();
            wrapper.Add("type", "hlvr_default_3d");
            wrapper.Add("vsnd_files", layers[i].File);
            wrapper.Add("delay", delay);

            if (falloffMax > 0f)
            {
                wrapper.Add("volume_falloff_min", falloffMin);
                wrapper.Add("volume_falloff_max", falloffMax);
            }

            definitions[i] = new SoundEventDefinition($"{Definition.Name}#layer{i}", wrapper);
        }

        return definitions;
    }
}
