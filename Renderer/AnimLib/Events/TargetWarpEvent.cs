using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TargetWarpEvent : Event
{
    public TargetWarpRule Rule { get; } = TargetWarpRule.WarpXYZ;
    public TargetWarpAlgorithm Algorithm { get; } = TargetWarpAlgorithm.Bezier;
}
