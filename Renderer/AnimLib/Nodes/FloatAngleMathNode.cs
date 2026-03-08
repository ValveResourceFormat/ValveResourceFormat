using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatAngleMathNode : FloatValueNode
{
    public short InputValueNodeIdx { get; }
    public FloatAngleMathNode__Operation Operation { get; }

    public FloatAngleMathNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        Operation = data.GetEnumValue<FloatAngleMathNode__Operation>("m_operation");
    }
}
