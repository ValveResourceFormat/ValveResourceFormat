using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A graph node that produces a value.</summary>
partial class ValueNode : GraphNode
{
    /// <summary>Reads the node definition from resource data.</summary>
    protected ValueNode(KVObject data) : base(data) { }
}
