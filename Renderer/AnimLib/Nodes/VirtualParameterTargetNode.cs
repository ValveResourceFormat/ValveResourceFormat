using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class VirtualParameterTargetNode : TargetValueNode
{
    public short ChildNodeIdx { get; } = -1;
}
