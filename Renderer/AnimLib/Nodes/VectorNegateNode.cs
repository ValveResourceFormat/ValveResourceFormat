using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class VectorNegateNode : VectorValueNode
{
    public short InputValueNodeIdx { get; }

    public VectorNegateNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
    }
}
