using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ConstVectorNode : VectorValueNode
{
    public Vector4 Value { get; }

    public ConstVectorNode(KVObject data) : base(data)
    {
        //Value = m_value;
    }
}
