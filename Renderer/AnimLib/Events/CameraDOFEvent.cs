using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class CameraDOFEvent : Event
{
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Curve { get; }
}
