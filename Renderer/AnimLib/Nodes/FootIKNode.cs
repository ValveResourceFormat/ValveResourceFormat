using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FootIKNode : PassthroughNode
{
    public GlobalSymbol LeftEffectorBoneID { get; }
    public GlobalSymbol RightEffectorBoneID { get; }
    public short LeftTargetNodeIdx { get; }
    public short RightTargetNodeIdx { get; }
    public short EnabledNodeIdx { get; }
    public float BlendTimeSeconds { get; }
    public IKBlendMode BlendMode { get; }
    public bool IsTargetInWorldSpace { get; }

    public FootIKNode(KVObject data) : base(data)
    {
        LeftEffectorBoneID = data.GetProperty<string>("m_leftEffectorBoneID");
        RightEffectorBoneID = data.GetProperty<string>("m_rightEffectorBoneID");
        LeftTargetNodeIdx = data.GetInt16Property("m_nLeftTargetNodeIdx");
        RightTargetNodeIdx = data.GetInt16Property("m_nRightTargetNodeIdx");
        EnabledNodeIdx = data.GetInt16Property("m_nEnabledNodeIdx");
        BlendTimeSeconds = data.GetFloatProperty("m_flBlendTimeSeconds");
        BlendMode = data.GetEnumValue<IKBlendMode>("m_blendMode");
        IsTargetInWorldSpace = data.GetProperty<bool>("m_bIsTargetInWorldSpace");
    }
}
