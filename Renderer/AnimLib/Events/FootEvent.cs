using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FootEvent : Event
{
    public FootPhase Phase { get; }
}
