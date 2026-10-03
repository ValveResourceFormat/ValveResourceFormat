using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class SelectorNode : PoseNode
{
    public short[] OptionNodeIndices { get; } = [];
    public short[] ConditionNodeIndices { get; } = [];
}
