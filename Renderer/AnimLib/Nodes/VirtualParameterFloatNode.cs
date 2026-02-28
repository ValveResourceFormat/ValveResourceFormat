using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class VirtualParameterFloatNode : FloatValueNode
{
    public short ChildNodeIdx { get; }

    public VirtualParameterFloatNode(KVObject data) : base(data)
    {
        ChildNodeIdx = data.GetInt16Property("m_nChildNodeIdx");
    }
}
