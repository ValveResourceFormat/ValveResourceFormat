using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class CameraFOVEvent : Event
{
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }
}
