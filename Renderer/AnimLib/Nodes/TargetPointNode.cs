using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TargetPointNode : VectorValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public bool IsWorldSpaceTarget { get; } = true;
}
