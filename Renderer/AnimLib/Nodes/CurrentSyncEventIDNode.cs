using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class CurrentSyncEventIDNode : IDValueNode
{
    public short SourceStateNodeIdx { get; }

    public CurrentSyncEventIDNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
    }
}
