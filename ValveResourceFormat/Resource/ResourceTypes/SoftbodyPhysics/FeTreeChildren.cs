using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// Child indices of a node in the collision bounding-volume tree.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeTreeChildren_t">FeTreeChildren_t</seealso>
public class FeTreeChildren
{
    /// <summary>The two child indices.</summary>
    public int[] Child { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeTreeChildren"/> class.
    /// </summary>
    /// <param name="data">The tree children key-value object.</param>
    public FeTreeChildren(KVObject data)
    {
        Child = data.GetInt32Array("nChild");
    }
}
