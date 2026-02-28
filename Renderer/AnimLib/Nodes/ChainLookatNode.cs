using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ChainLookatNode : PassthroughNode
{
    public GlobalSymbol ChainEndBoneID { get; }
    public short LookatTargetNodeIdx { get; }
    public short EnabledNodeIdx { get; }
    public float BlendTimeSeconds { get; }
    public byte ChainLength { get; }
    public bool IsTargetInWorldSpace { get; }
    public Vector3 ChainForwardDir { get; }

    public ChainLookatNode(KVObject data) : base(data)
    {
        ChainEndBoneID = data.GetProperty<string>("m_chainEndBoneID");
        LookatTargetNodeIdx = data.GetInt16Property("m_nLookatTargetNodeIdx");
        EnabledNodeIdx = data.GetInt16Property("m_nEnabledNodeIdx");
        BlendTimeSeconds = data.GetFloatProperty("m_flBlendTimeSeconds");
        ChainLength = data.GetByteProperty("m_nChainLength");
        IsTargetInWorldSpace = data.GetProperty<bool>("m_bIsTargetInWorldSpace");
        ChainForwardDir = data.GetSubCollection("m_chainForwardDir").ToVector3();
    }
}
