using GUI.Types.Graphs.Core;
using ValveKeyValue;

namespace GUI.Types.Graphs;

/// <summary>
/// Graph node carrying its source <see cref="KVObject"/> and exposing the Name/NodeType
/// naming used by the resource graph frontends.
/// </summary>
class KVGraphNode : GraphNode
{
    public KVObject? Data { get; set; }

    public KVGraphNode(KVObject? data)
    {
        Data = data;
    }

    public string? Name
    {
        get => Title;
        set => Title = value ?? string.Empty;
    }

    public string NodeType
    {
        get => Subtitle ?? string.Empty;
        set => Subtitle = value;
    }
}
