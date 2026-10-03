using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class IsExternalGraphSlotFilledNode : BoolValueNode
{
    public short ExternalGraphNodeIdx { get; }

    public IsExternalGraphSlotFilledNode(KVObject data) : base(data)
    {
        ExternalGraphNodeIdx = data.GetInt16Property("m_nExternalGraphNodeIdx");
    }
}
