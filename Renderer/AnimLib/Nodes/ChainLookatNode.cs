using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ChainLookatNode : PassthroughNode
{
    public GlobalSymbol EndEffectorBoneID { get; }
    public Vector3 EndEffectorForwardAxis { get; }
    public Vector3 EndEffectorOffset { get; }
    public short LookatTargetNodeIdx { get; }
    public short EnabledNodeIdx { get; }
    public float BlendTimeSeconds { get; }
    public float[] ChainWeights { get; }
    public byte ChainLength { get; }
    public bool IsTargetInWorldSpace { get; }

    public ChainLookatNode(KVObject data) : base(data)
    {
        EndEffectorBoneID = data.GetProperty<string>("m_endEffectorBoneID");
        EndEffectorForwardAxis = data.GetSubCollection("m_endEffectorForwardAxis").ToVector3();
        EndEffectorOffset = data.GetSubCollection("m_endEffectorOffset").ToVector3();
        LookatTargetNodeIdx = data.GetInt16Property("m_nLookatTargetNodeIdx");
        EnabledNodeIdx = data.GetInt16Property("m_nEnabledNodeIdx");
        BlendTimeSeconds = data.GetFloatProperty("m_flBlendTimeSeconds");
        ChainWeights = data.GetArray<float>("m_chainWeights") ?? [];
        ChainLength = data.GetByteProperty("m_nChainLength");
        IsTargetInWorldSpace = data.GetProperty<bool>("m_bIsTargetInWorldSpace");
    }
}
