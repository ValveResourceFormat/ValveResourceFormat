using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class NotNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
}
