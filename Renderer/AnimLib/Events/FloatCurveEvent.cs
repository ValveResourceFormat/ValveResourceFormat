using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class FloatCurveEvent : Event
{
    public GlobalSymbol ID { get; }
    public Particles.Utils.PiecewiseCurve Curve { get; }

    public FloatCurveEvent(KVObject data) : base(data)
    {
        ID = data.GetProperty<string>("m_ID");
        Curve = new(data.GetProperty<KVObject>("m_curve"), false);
    }
}
