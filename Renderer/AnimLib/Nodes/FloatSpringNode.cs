using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatSpringNode : FloatValueNode
{
    public float StartValue { get; }
    public float Hertz { get; } = 4f;
    public float DampingRatio { get; } = 0.7f;
    public short InputValueNodeIdx { get; } = -1;
    public bool UseStartValue { get; }
}
