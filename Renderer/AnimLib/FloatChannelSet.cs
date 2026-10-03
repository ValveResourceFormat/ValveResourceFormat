using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class FloatChannelSet
{
    public GlobalSymbol ID { get; }
    public GlobalSymbol[] ChannelIDs { get; }

    public FloatChannelSet(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        ChannelIDs = data.GetSymbolArray("m_channelIDs");
    }
}
