using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class SyncEventIndexConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public SyncEventIndexConditionNode__TriggerMode TriggerMode { get; }
    [KVProperty("m_syncEventIdx")]
    public int SyncEventIdx { get; } = -1;
}
