using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class VirtualParameterBoneMaskNode : BoneMaskValueNode
{
    public short ChildNodeIdx { get; } = -1;
}
