using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>The sounds a contact point plays.</summary>
[KV3Transfer]
public partial class ContactAudioInfo
{
    /// <summary>The audio action ID.</summary>
    public GlobalSymbol AudioActionID { get; }
    /// <summary>The audio type ID.</summary>
    public GlobalSymbol AudioTypeID { get; }
    /// <summary>The sound event to play instead of the default.</summary>
    public GlobalSymbol SoundeventOverrideID { get; }
}
