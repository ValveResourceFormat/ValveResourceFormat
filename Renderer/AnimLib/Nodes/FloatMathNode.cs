using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatMathNode : FloatValueNode
{
    public short InputValueNodeIdxA { get; } = -1;
    public short InputValueNodeIdxB { get; } = -1;
    public bool ReturnAbsoluteResult { get; }
    public bool ReturnNegatedResult { get; }
    public FloatMathNode.OperatorType Operator { get; }
    public float ValueB { get; }
}
