using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class IDSwitchNode : IDValueNode
{
    public short SwitchValueNodeIdx { get; }
    public short TrueValueNodeIdx { get; }
    public short FalseValueNodeIdx { get; }
    public GlobalSymbol FalseValue { get; }
    public GlobalSymbol TrueValue { get; }

    public IDSwitchNode(KVObject data) : base(data)
    {
        SwitchValueNodeIdx = data.GetInt16Property("m_nSwitchValueNodeIdx");
        TrueValueNodeIdx = data.GetInt16Property("m_nTrueValueNodeIdx");
        FalseValueNodeIdx = data.GetInt16Property("m_nFalseValueNodeIdx");
        FalseValue = data.GetProperty<string>("m_falseValue");
        TrueValue = data.GetProperty<string>("m_trueValue");
    }
}
