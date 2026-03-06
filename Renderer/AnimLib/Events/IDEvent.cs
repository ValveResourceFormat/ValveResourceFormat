using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class IDEvent : Event
{
    public GlobalSymbol ID { get; }
    public GlobalSymbol SecondaryID { get; }

    public IDEvent(KVObject data) : base(data)
    {
        ID = data.GetProperty<string>("m_ID");
        SecondaryID = data.GetProperty<string>("m_secondaryID");
    }
}
