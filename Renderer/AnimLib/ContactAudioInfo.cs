using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class ContactAudioInfo
{
    public GlobalSymbol AudioActionID { get; }
    public GlobalSymbol AudioTypeID { get; }
    public GlobalSymbol SoundeventOverrideID { get; }

    public ContactAudioInfo(KVObject data)
    {
        AudioActionID = data.GetProperty<string>("m_audioActionID");
        AudioTypeID = data.GetProperty<string>("m_audioTypeID");
        SoundeventOverrideID = data.GetProperty<string>("m_soundeventOverrideID");
    }
}
