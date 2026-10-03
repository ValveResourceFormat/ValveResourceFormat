using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public short[] ConditionNodeIndices { get; } = [];
}
