using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class BoneMaskBlendNode : BoneMaskValueNode
{
    public short SourceMaskNodeIdx { get; }
    public short TargetMaskNodeIdx { get; }
    public short BlendWeightValueNodeIdx { get; }

    public BoneMaskBlendNode(KVObject data) : base(data)
    {
        SourceMaskNodeIdx = data.GetInt16Property("m_nSourceMaskNodeIdx");
        TargetMaskNodeIdx = data.GetInt16Property("m_nTargetMaskNodeIdx");
        BlendWeightValueNodeIdx = data.GetInt16Property("m_nBlendWeightValueNodeIdx");
    }
}
