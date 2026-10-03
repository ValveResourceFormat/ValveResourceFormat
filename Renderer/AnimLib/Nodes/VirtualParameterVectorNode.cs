using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class VirtualParameterVectorNode : VectorValueNode
{
    public short ChildNodeIdx { get; } = -1;
}
