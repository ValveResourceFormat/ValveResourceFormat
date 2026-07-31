using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class CurrentSyncEventNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; }
    public CurrentSyncEventNode__InfoType InfoType { get; }

    public CurrentSyncEventNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        InfoType = data.GetEnumValue<CurrentSyncEventNode__InfoType>("m_infoType");
    }
}
