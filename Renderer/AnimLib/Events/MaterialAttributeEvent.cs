using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class MaterialAttributeEvent : Event
{
    public EventTargetEntity Target { get; }
    public string AttributeName { get; }
    public GlobalSymbol AttributeNameToken { get; }
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve X { get; }
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Y { get; }
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve Z { get; }
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve W { get; }
}
