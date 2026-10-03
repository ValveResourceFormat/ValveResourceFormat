using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ScaleNode : PassthroughNode
{
    public short MaskNodeIdx { get; } = -1;
    public short EnableNodeIdx { get; } = -1;
}
