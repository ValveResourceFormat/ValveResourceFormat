using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatSwitchNode : FloatValueNode
{
    public short SwitchValueNodeIdx { get; }
    public short TrueValueNodeIdx { get; }
    public short FalseValueNodeIdx { get; }
    public float FalseValue { get; }
    public float TrueValue { get; }

    public FloatSwitchNode(KVObject data) : base(data)
    {
        SwitchValueNodeIdx = data.GetInt16Property("m_nSwitchValueNodeIdx");
        TrueValueNodeIdx = data.GetInt16Property("m_nTrueValueNodeIdx");
        FalseValueNodeIdx = data.GetInt16Property("m_nFalseValueNodeIdx");
        FalseValue = data.GetFloatProperty("m_flFalseValue");
        TrueValue = data.GetFloatProperty("m_flTrueValue");
    }
}
