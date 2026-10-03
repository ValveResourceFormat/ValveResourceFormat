using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A graph node that produces an ID value.</summary>
partial class IDValueNode : ValueNode
{
    /// <summary>Reads the node definition from resource data.</summary>
    public IDValueNode(KVObject data) : base(data) { }
}
