using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class CurrentSyncEventIDNode : IDValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
}
