using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class BoneMaskSwitchNode : BoneMaskValueNode
{
    public short SwitchValueNodeIdx { get; }
    public short TrueValueNodeIdx { get; }
    public short FalseValueNodeIdx { get; }
    public float BlendTimeSeconds { get; }
    public bool SwitchDynamically { get; }

    public BoneMaskSwitchNode(KVObject data) : base(data)
    {
        SwitchValueNodeIdx = data.GetInt16Property("m_nSwitchValueNodeIdx");
        TrueValueNodeIdx = data.GetInt16Property("m_nTrueValueNodeIdx");
        FalseValueNodeIdx = data.GetInt16Property("m_nFalseValueNodeIdx");
        BlendTimeSeconds = data.GetFloatProperty("m_flBlendTimeSeconds");
        SwitchDynamically = data.GetProperty<bool>("m_bSwitchDynamically");
    }
}
