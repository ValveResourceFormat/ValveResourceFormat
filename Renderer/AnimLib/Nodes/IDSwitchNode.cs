using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class IDSwitchNode : IDValueNode
{
    public short SwitchValueNodeIdx { get; } = -1;
    public short TrueValueNodeIdx { get; } = -1;
    public short FalseValueNodeIdx { get; } = -1;
    public GlobalSymbol FalseValue { get; }
    public GlobalSymbol TrueValue { get; }
}
