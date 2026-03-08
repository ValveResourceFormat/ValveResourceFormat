using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class ParameterizedClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; }
    public byte[] OptionWeights { get; }
    public short ParameterNodeIdx { get; }
    public bool IgnoreInvalidOptions { get; }
    public bool HasWeightsSet { get; }

    public ParameterizedClipSelectorNode(KVObject data) : base(data)
    {
        OptionNodeIndices = data.GetArray<short>("m_optionNodeIndices");
        OptionWeights = data.GetArray<byte>("m_optionWeights");
        ParameterNodeIdx = data.GetInt16Property("m_parameterNodeIdx");
        IgnoreInvalidOptions = data.GetProperty<bool>("m_bIgnoreInvalidOptions");
        HasWeightsSet = data.GetProperty<bool>("m_bHasWeightsSet");
    }
}
