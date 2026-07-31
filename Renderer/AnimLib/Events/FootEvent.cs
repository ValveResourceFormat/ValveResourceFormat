using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class FootEvent : Event
{
    public FootPhase Phase { get; }

    public FootEvent(KVObject data) : base(data)
    {
        Phase = data.GetEnumValue<FootPhase>("m_phase");
    }
}
