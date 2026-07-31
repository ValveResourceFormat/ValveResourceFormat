using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FollowBoneNode : PassthroughNode
{
    public GlobalSymbol Bone { get; }
    public GlobalSymbol FollowTargetBone { get; }
    public short EnabledNodeIdx { get; }
    public FollowBoneMode Mode { get; }

    public FollowBoneNode(KVObject data) : base(data)
    {
        Bone = data.GetProperty<string>("m_bone");
        FollowTargetBone = data.GetProperty<string>("m_followTargetBone");
        EnabledNodeIdx = data.GetInt16Property("m_nEnabledNodeIdx");
        Mode = data.GetEnumValue<FollowBoneMode>("m_mode");
    }
}
