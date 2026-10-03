using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A graph node that produces a float value.</summary>
partial class FloatValueNode : ValueNode
{
    /// <summary>Reads the node definition from resource data.</summary>
    public FloatValueNode(KVObject data) : base(data) { }
}
