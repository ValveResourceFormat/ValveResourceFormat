using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class CachedIDNode : IDValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public CachedValueMode Mode { get; }
}
