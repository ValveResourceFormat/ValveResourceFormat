using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class IsExternalGraphSlotFilledNode : BoolValueNode
{
    public short ExternalGraphNodeIdx { get; } = -1;
}
