using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ConstBoolNode : BoolValueNode
{
    public bool Value { get; }
}
