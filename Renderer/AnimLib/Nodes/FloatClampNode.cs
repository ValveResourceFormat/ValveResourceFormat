using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatClampNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public Range ClampRange { get; }
}
