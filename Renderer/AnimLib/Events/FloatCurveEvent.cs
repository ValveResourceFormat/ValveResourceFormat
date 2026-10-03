using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatCurveEvent : Event
{
    public GlobalSymbol ID { get; }
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }
}
