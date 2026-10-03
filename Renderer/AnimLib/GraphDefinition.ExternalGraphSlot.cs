using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class GraphDefinition
{
    [KV3Transfer]
    internal partial class ExternalGraphSlot
    {
        public short NodeIdx { get; } = -1;
        public GlobalSymbol SlotID { get; }
    }
}
