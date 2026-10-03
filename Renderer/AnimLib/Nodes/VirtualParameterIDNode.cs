using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class VirtualParameterIDNode : IDValueNode
{
    public short ChildNodeIdx { get; } = -1;
}
