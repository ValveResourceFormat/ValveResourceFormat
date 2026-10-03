using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class AndNode : BoolValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
}
