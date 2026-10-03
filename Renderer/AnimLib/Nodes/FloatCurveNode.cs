using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatCurveNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }
}
