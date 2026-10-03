using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class CurrentSyncEventNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public CurrentSyncEventNode.CurrentSyncEventNodeInfoType InfoType { get; }
}
