using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class SyncEventIndexConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; }
    public SyncEventIndexConditionNode__TriggerMode TriggerMode { get; }
    public int SyncEventIdx { get; }

    public SyncEventIndexConditionNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        TriggerMode = data.GetEnumValue<SyncEventIndexConditionNode__TriggerMode>("m_triggerMode");
        SyncEventIdx = data.GetInt32Property("m_syncEventIdx");
    }
}
