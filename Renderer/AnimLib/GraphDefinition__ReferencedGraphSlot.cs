using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class GraphDefinition__ReferencedGraphSlot
{
    public short NodeIdx { get; }
    public short DataSlotIdx { get; }

    public GraphDefinition__ReferencedGraphSlot(KVObject data)
    {
        NodeIdx = data.GetInt16Property("m_nNodeIdx");
        DataSlotIdx = data.GetInt16Property("m_dataSlotIdx");
    }
}
