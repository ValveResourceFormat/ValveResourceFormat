using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Animation;

/// <summary>A named set of float animation channels.</summary>
[KV3Transfer]
public partial class FloatChannelSet
{
    /// <summary>The set ID.</summary>
    public StringToken ID { get; }
    /// <summary>The channels in the set.</summary>
    public StringToken[] ChannelIDs { get; } = [];
}
