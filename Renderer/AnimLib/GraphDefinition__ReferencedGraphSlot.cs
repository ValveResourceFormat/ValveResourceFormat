using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class GraphDefinition__ReferencedGraphSlot
{
    public short NodeIdx { get; } = -1;
    [KVProperty("m_dataSlotIdx")]
    public short DataSlotIdx { get; } = -1;
}
