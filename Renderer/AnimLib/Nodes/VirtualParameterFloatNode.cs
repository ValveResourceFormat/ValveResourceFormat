using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class VirtualParameterFloatNode : FloatValueNode
{
    public short ChildNodeIdx { get; } = -1;
}
