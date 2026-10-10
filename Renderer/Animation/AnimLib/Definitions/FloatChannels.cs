using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatChannelData
{
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
    public GlobalSymbol SetID { get; }
    public FloatChannelData.ChannelSettingsType[] ChannelSettings { get; } = [];
    public ushort[] CompressedData { get; } = [];
    public uint[] CompressedOffsets { get; } = [];
}

partial class FloatChannelData
{
    [KV3Transfer]
    internal partial class ChannelSettingsType
    {
        public CompressionSettings.QuantizationRange Range { get; }
        public bool IsStatic { get; }
    }
}

/// <summary>A named set of float animation channels.</summary>
[KV3Transfer]
public partial class FloatChannelSet
{
    /// <summary>The set ID.</summary>
    public GlobalSymbol ID { get; }
    /// <summary>The channels in the set.</summary>
    public GlobalSymbol[] ChannelIDs { get; } = [];
}

[KV3Transfer]
partial class FloatCurveCompressionSettings
{
    public CompressionSettings.QuantizationRange Range { get; }
    public bool IsStatic { get; }
}
