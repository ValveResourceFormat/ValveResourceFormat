using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ParameterizedClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public byte[] OptionWeights { get; } = [];
    [KVProperty("m_parameterNodeIdx")]
    public short ParameterNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
    public bool HasWeightsSet { get; }
}
