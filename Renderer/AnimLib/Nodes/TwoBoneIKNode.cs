using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TwoBoneIKNode : PassthroughNode
{
    public GlobalSymbol EffectorBoneID { get; }
    public short EffectorTargetNodeIdx { get; }
    public short EnabledNodeIdx { get; }
    public float BlendTimeSeconds { get; }
    public IKBlendMode BlendMode { get; }
    public bool IsTargetInWorldSpace { get; }
    public float ReferencePoseTwistWeight { get; }

    public TwoBoneIKNode(KVObject data) : base(data)
    {
        EffectorBoneID = data.GetProperty<string>("m_effectorBoneID");
        EffectorTargetNodeIdx = data.GetInt16Property("m_nEffectorTargetNodeIdx");
        EnabledNodeIdx = data.GetInt16Property("m_nEnabledNodeIdx");
        BlendTimeSeconds = data.GetFloatProperty("m_flBlendTimeSeconds");
        BlendMode = data.GetEnumValue<IKBlendMode>("m_blendMode");
        IsTargetInWorldSpace = data.GetProperty<bool>("m_bIsTargetInWorldSpace");
        ReferencePoseTwistWeight = data.GetFloatProperty("m_flReferencePoseTwistWeight");
    }
}
