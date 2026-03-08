using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class VectorInfoNode : FloatValueNode
{
    public short InputValueNodeIdx { get; }
    public VectorInfoNode__Info DesiredInfo { get; }

    public VectorInfoNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        DesiredInfo = data.GetEnumValue<VectorInfoNode__Info>("m_desiredInfo");
    }
}
