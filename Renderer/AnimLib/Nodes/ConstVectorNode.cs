using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ConstVectorNode : VectorValueNode
{
    public Vector3 Value { get; }

    public ConstVectorNode(KVObject data) : base(data)
    {
        Value = data.GetSubCollection("m_value").ToVector3();
    }
}
