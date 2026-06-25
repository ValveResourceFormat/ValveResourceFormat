using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// Makes a child node follow a parent node by a weight.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeFollowNode_t">FeFollowNode_t</seealso>
public class FeFollowNode
{
    /// <summary>Index of the parent node that is followed.</summary>
    public int ParentNode { get; }

    /// <summary>Index of the child node that follows.</summary>
    public int ChildNode { get; }

    /// <summary>Strength of the follow constraint.</summary>
    public float Weight { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeFollowNode"/> class.
    /// </summary>
    /// <param name="data">The follow node key-value object.</param>
    public FeFollowNode(KVObject data)
    {
        ParentNode = data.GetInt32Property("nParentNode");
        ChildNode = data.GetInt32Property("nChildNode");
        Weight = data.GetFloatProperty("flWeight");
    }
}
