using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class VirtualParameterIDNode : IDValueNode
{
    public short ChildNodeIdx { get; }

    public VirtualParameterIDNode(KVObject data) : base(data)
    {
        ChildNodeIdx = data.GetInt16Property("m_nChildNodeIdx");
    }
}
