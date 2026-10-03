using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class OrNode : BoolValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
}
