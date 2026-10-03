using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A graph node that produces a pose.</summary>
partial class PoseNode : GraphNode
{
    /// <summary>Reads the node definition from resource data.</summary>
    public PoseNode(KVObject data) : base(data) { }
}
