using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Animation;

[KV3Transfer] public partial class ContactAudioActionVData { }

/// <summary>The sounds a contact point plays.</summary>
[KV3Transfer]
public partial class ContactAudioInfo
{
    /// <summary>The audio action ID.</summary>
    public StringToken AudioActionID { get; }
    /// <summary>The audio type ID.</summary>
    public StringToken AudioTypeID { get; }
    /// <summary>The sound event to play instead of the default.</summary>
    public StringToken SoundeventOverrideID { get; }
}

[KV3Transfer] public partial class ContactAudioTypeVData { }
