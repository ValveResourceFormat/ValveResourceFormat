using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class VirtualParameterTargetNode : TargetValueNode
{
    public short ChildNodeIdx { get; }

    public VirtualParameterTargetNode(KVObject data) : base(data)
    {
        ChildNodeIdx = data.GetInt16Property("m_nChildNodeIdx");
    }
}
