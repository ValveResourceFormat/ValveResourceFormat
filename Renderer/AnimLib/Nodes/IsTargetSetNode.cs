using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class IsTargetSetNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
}
