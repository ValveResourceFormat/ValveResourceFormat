using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class CachedBoolNode : BoolValueNode
{
    public short InputValueNodeIdx { get; }
    public CachedValueMode Mode { get; }

    public CachedBoolNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        Mode = data.GetEnumValue<CachedValueMode>("m_mode");
    }
}
