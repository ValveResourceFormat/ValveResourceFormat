using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ConstFloatNode : FloatValueNode
{
    public float Value { get; }
}
