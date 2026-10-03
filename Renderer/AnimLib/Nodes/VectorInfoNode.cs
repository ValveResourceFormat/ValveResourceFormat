using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class VectorInfoNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public VectorInfoNode.Info DesiredInfo { get; }
}
