using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A named set of float animation channels.</summary>
public class FloatChannelSet
{
    /// <summary>The set ID.</summary>
    public GlobalSymbol ID { get; }
    /// <summary>The channels in the set.</summary>
    public GlobalSymbol[] ChannelIDs { get; }

    /// <summary>Reads the set from resource data.</summary>
    public FloatChannelSet(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        ChannelIDs = data.GetSymbolArray("m_channelIDs");
    }
}
