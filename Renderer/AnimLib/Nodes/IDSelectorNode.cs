using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class IDSelectorNode : IDValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
    public GlobalSymbol[] Values { get; } = [];
    public GlobalSymbol DefaultValue { get; }
}
