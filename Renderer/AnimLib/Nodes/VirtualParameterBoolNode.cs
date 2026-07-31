using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class VirtualParameterBoolNode : BoolValueNode
{
    public short ChildNodeIdx { get; }

    public VirtualParameterBoolNode(KVObject data) : base(data)
    {
        ChildNodeIdx = data.GetInt16Property("m_nChildNodeIdx");
    }
}
