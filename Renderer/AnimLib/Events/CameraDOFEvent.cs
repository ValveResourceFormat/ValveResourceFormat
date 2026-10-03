using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class CameraDOFEvent : Event
{
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }

    public CameraDOFEvent(KVObject data) : base(data)
    {
        Curve = new(data.GetProperty<KVObject>("m_curve"), false);
    }
}
