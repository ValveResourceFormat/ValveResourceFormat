using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

// Valve extension: selects a pose option by matching an ID parameter against per-option IDs.
partial class IDBasedSelectorNode : PoseNode
{
    public short[] OptionNodeIndices { get; }
    public GlobalSymbol[] OptionIDs { get; }
    public short ParameterNodeIdx { get; }
    public short FallbackNodeIdx { get; }
    public bool IgnoreInvalidOptions { get; }

    public IDBasedSelectorNode(KVObject data) : base(data)
    {
        OptionNodeIndices = data.GetArray<short>("m_optionNodeIndices");
        OptionIDs = data.GetSymbolArray("m_optionIDs");
        ParameterNodeIdx = data.GetInt16Property("m_nParameterNodeIdx");
        FallbackNodeIdx = data.GetInt16Property("m_nFallbackNodeIdx");
        IgnoreInvalidOptions = data.GetProperty<bool>("m_bIgnoreInvalidOptions");
    }
}
