using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class GraphDefinition__ExternalPoseSlot
{
    public short NodeIdx { get; }
    public GlobalSymbol SlotID { get; }

    public GraphDefinition__ExternalPoseSlot(KVObject data)
    {
        NodeIdx = data.GetInt16Property("m_nNodeIdx");
        SlotID = data.GetProperty<string>("m_slotID");
    }
}
