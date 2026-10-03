using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ConstTargetNode : TargetValueNode
{
    public Target Value { get; }
}
