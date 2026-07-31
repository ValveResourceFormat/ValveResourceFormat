using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatMathNode : FloatValueNode
{
    public short InputValueNodeIdxA { get; }
    public short InputValueNodeIdxB { get; }
    public bool ReturnAbsoluteResult { get; }
    public bool ReturnNegatedResult { get; }
    public FloatMathNode__Operator Operator { get; }
    public float ValueB { get; }

    public FloatMathNode(KVObject data) : base(data)
    {
        InputValueNodeIdxA = data.GetInt16Property("m_nInputValueNodeIdxA");
        InputValueNodeIdxB = data.GetInt16Property("m_nInputValueNodeIdxB");
        ReturnAbsoluteResult = data.GetProperty<bool>("m_bReturnAbsoluteResult");
        ReturnNegatedResult = data.GetProperty<bool>("m_bReturnNegatedResult");
        Operator = data.GetEnumValue<FloatMathNode__Operator>("m_operator");
        ValueB = data.GetFloatProperty("m_flValueB");
    }
}
