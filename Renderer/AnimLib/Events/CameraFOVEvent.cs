using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class CameraFOVEvent : Event
{
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }

    public CameraFOVEvent(KVObject data) : base(data)
    {
        Curve = new(data.GetProperty<KVObject>("m_curve"), false);
    }
}
