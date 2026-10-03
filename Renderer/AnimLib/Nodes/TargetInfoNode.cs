using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TargetInfoNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public TargetInfoNode.Info InfoType { get; } = TargetInfoNode.Info.Distance;
    public bool IsWorldSpaceTarget { get; } = true;
}
