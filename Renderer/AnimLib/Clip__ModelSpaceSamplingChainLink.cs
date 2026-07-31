using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class Clip__ModelSpaceSamplingChainLink
{
    public int BoneIdx { get; }
    public int ParentBoneIdx { get; }
    public int ParentChainLinkIdx { get; }

    public Clip__ModelSpaceSamplingChainLink(KVObject data)
    {
        BoneIdx = data.GetInt32Property("m_nBoneIdx");
        ParentBoneIdx = data.GetInt32Property("m_nParentBoneIdx");
        ParentChainLinkIdx = data.GetInt32Property("m_nParentChainLinkIdx");
    }
}
