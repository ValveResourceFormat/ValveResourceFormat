using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FixedWeightBoneMaskNode : BoneMaskValueNode
{
    public float BoneWeight { get; }

    public FixedWeightBoneMaskNode(KVObject data) : base(data)
    {
        BoneWeight = data.GetFloatProperty("m_flBoneWeight");
    }
}
