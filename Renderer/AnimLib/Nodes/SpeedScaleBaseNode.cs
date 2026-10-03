using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class SpeedScaleBaseNode : PassthroughNode
{
    public short InputValueNodeIdx { get; } = -1;
    public float DefaultInputValue { get; }
}
