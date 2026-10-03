using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A node of an animation graph.</summary>
[KV3Transfer]
partial class GraphNode
{
    /// <summary>The index of the node in the graph.</summary>
    public short NodeIdx { get; }
}
