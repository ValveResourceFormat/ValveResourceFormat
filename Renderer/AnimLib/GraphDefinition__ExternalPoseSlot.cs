using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class GraphDefinition__ExternalPoseSlot
{
    public short NodeIdx { get; } = -1;
    public GlobalSymbol SlotID { get; }
}
