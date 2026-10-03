using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class VirtualParameterVectorNode : VectorValueNode
{
    public short ChildNodeIdx { get; }

    public VirtualParameterVectorNode(KVObject data) : base(data)
    {
        ChildNodeIdx = data.GetInt16Property("m_nChildNodeIdx");
    }
}
