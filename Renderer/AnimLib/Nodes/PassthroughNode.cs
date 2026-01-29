using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class PassthroughNode : PoseNode
{
    public short ChildNodeIdx { get; }

    public PassthroughNode(KVObject data) : base(data)
    {
        ChildNodeIdx = data.GetInt16Property("m_nChildNodeIdx");
    }
}
