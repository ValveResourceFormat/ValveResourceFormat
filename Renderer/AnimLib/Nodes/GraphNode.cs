using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class GraphNode
{
    public short NodeIdx { get; }

    public GraphNode(KVObject data)
    {
        NodeIdx = data.GetInt16Property("m_nNodeIdx");
    }
}
