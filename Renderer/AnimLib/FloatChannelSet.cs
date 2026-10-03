using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A named set of float animation channels.</summary>
[KV3Transfer]
public partial class FloatChannelSet
{
    /// <summary>The set ID.</summary>
    public GlobalSymbol ID { get; }
    /// <summary>The channels in the set.</summary>
    public GlobalSymbol[] ChannelIDs { get; } = [];
}
