using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class GraphDefinition__ExternalGraphSlot
{
    public short NodeIdx { get; } = -1;
    public GlobalSymbol SlotID { get; }
}
