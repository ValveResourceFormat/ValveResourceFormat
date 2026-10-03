using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A node of an animation graph.</summary>
partial class GraphNode
{
    /// <summary>The index of the node in the graph.</summary>
    public short NodeIdx { get; }

    /// <summary>Reads the node definition from resource data.</summary>
    protected GraphNode(KVObject data)
    {
        NodeIdx = data.GetInt16Property("m_nNodeIdx");
    }
}
