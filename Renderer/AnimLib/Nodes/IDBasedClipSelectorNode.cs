using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

// Valve extension: selects a clip option by matching an ID parameter against per-option IDs.
[KV3Transfer]
partial class IDBasedClipSelectorNode : ClipReferenceNode
{
    public short[] OptionNodeIndices { get; } = [];
    public GlobalSymbol[] OptionIDs { get; } = [];
    public short ParameterNodeIdx { get; } = -1;
    public short FallbackNodeIdx { get; } = -1;
    public bool IgnoreInvalidOptions { get; }
}
