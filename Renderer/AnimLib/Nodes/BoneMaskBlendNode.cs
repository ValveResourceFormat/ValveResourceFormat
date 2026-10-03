using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class BoneMaskBlendNode : BoneMaskValueNode
{
    public short SourceMaskNodeIdx { get; } = -1;
    public short TargetMaskNodeIdx { get; } = -1;
    public short BlendWeightValueNodeIdx { get; } = -1;
}
