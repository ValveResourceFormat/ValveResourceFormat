using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatCurveNode : FloatValueNode
{
    public short InputValueNodeIdx { get; }
    public Particles.Utils.PiecewiseCurve Curve { get; }

    public FloatCurveNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        Curve = new(data.GetProperty<KVObject>("m_curve"), false);
    }
}
