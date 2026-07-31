using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the layered weapon sound types "hlvr_gun_layers_3d" (mid/mech/lfe/distant) and
/// "hlvr_player_gun_layers_3d" (head/body/lfe/tail): one shot built from several tracks played together,
/// each with its own volume and distance falloff, so the mix shifts from body to tail with distance.
/// </summary>
/// <remarks>
/// The distant layer's authored crossfade ("distant_xfade_start"/"distant_xfade_end") is not modeled - the
/// per-layer falloff ranges already fade the layers in and out over distance. Each layer is wrapped as an
/// anonymous "hlvr_default_3d" child (see <see cref="SoundEventHLVRDefault"/>) rather than given its own
/// sample-provider plumbing, the same way <see cref="SoundEventHLVRAmbientMultiVsnd"/> handles its layers.
/// </remarks>
internal sealed class SoundEventHLVRGunLayers : SoundEvent
{
    private static readonly string[] NpcLayers = ["mid", "mech", "lfe", "distant"];
    private static readonly string[] PlayerLayers = ["head", "body", "lfe", "tail"];

    private readonly (string File, float Volume, float FalloffMin, float FalloffMax)[] layers;
    private readonly string mixGroup;
    private readonly float pitchRandMin;
    private readonly float pitchRandMax;
    private readonly Action<SoundEvent, int> applyLayer;
    private float startBaseVolume;

    public SoundEventHLVRGunLayers(SoundEventDefinition definition) : base(definition)
    {
        applyLayer = ApplyLayer;

        var data = definition.Data;
        var list = new List<(string, float, float, float)>();

        foreach (var layer in definition.Type == "hlvr_player_gun_layers_3d" ? PlayerLayers : NpcLayers)
        {
            var file = data.GetStringProperty("vsnd_files_" + layer);

            if (!string.IsNullOrEmpty(file))
            {
                list.Add((
                    file,
                    data.GetFloatProperty("volume_" + layer, 1f),
                    data.GetFloatProperty("volume_falloff_" + layer + "_min"),
                    data.GetFloatProperty("volume_falloff_" + layer + "_max")));
            }
        }

        layers = [.. list];
        mixGroup = data.GetStringProperty("mixgroup", string.Empty);
        pitchRandMin = data.GetFloatProperty("pitch_rand_min");
        pitchRandMax = data.GetFloatProperty("pitch_rand_max");
    }

    protected override void DoStart()
    {
        if (layers.Length == 0)
        {
            return;
        }

        startBaseVolume = Math.Clamp(VolumeOverride ?? Definition.Volume, 0f, 1f) * Mixer.Player.GetMixGroupVolume(mixGroup);

        StartChildren(Definition.ChildDefinitions ??= BuildLayerDefinitions(), applyLayer);
    }

    private void ApplyLayer(SoundEvent child, int i)
    {
        child.Position = Position;
        child.VolumeOverride = startBaseVolume * layers[i].Volume;
    }

    internal override void Prewarm(int depth)
    {
        if (layers.Length > 0)
        {
            PrewarmChildren(Definition.ChildDefinitions ??= BuildLayerDefinitions(), depth);
        }
    }

    private SoundEventDefinition[] BuildLayerDefinitions()
    {
        var definitions = new SoundEventDefinition[layers.Length];

        for (var i = 0; i < layers.Length; i++)
        {
            var (file, _, falloffMin, falloffMax) = layers[i];

            var wrapper = new KVObject();
            wrapper.Add("type", "hlvr_default_3d");
            wrapper.Add("vsnd_files", file);
            wrapper.Add("delay", Definition.Delay);
            wrapper.Add("pitch", Definition.Pitch);
            wrapper.Add("pitch_rand_min", pitchRandMin);
            wrapper.Add("pitch_rand_max", pitchRandMax);

            if (falloffMax > 0f)
            {
                wrapper.Add("volume_falloff_min", falloffMin);
                wrapper.Add("volume_falloff_max", falloffMax);
            }

            definitions[i] = new SoundEventDefinition($"{Definition.Name}#{i}", wrapper);
        }

        return definitions;
    }
}
