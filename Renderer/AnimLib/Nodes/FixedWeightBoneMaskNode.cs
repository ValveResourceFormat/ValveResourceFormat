using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FixedWeightBoneMaskNode : BoneMaskValueNode
{
    public float BoneWeight { get; }
}
