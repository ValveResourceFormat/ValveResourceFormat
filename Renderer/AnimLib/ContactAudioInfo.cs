using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>The sounds a contact point plays.</summary>
public class ContactAudioInfo
{
    /// <summary>The audio action ID.</summary>
    public GlobalSymbol AudioActionID { get; }
    /// <summary>The audio type ID.</summary>
    public GlobalSymbol AudioTypeID { get; }
    /// <summary>The sound event to play instead of the default.</summary>
    public GlobalSymbol SoundeventOverrideID { get; }

    /// <summary>Reads the info from resource data.</summary>
    public ContactAudioInfo(KVObject data)
    {
        AudioActionID = data.GetProperty<string>("m_audioActionID");
        AudioTypeID = data.GetProperty<string>("m_audioTypeID");
        SoundeventOverrideID = data.GetProperty<string>("m_soundeventOverrideID");
    }
}
