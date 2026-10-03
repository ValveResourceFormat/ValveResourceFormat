using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class VirtualParameterBoolNode : BoolValueNode
{
    public short ChildNodeIdx { get; } = -1;
}
