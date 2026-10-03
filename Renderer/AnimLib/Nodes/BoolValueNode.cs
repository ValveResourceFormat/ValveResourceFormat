using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A graph node that produces a boolean value.</summary>
partial class BoolValueNode : ValueNode
{
    /// <summary>Reads the node definition from resource data.</summary>
    public BoolValueNode(KVObject data) : base(data) { }
}
