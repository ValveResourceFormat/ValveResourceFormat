using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class GraphDefinition
{
    [KV3Transfer]
    internal partial class ReferencedGraphSlot
    {
        public short NodeIdx { get; } = -1;
        [KVProperty("m_dataSlotIdx")]
        public short DataSlotIdx { get; } = -1;
    }
}
