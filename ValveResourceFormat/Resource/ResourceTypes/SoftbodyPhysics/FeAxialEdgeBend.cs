using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// An axial edge bend constraint.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeAxialEdgeBend_t">FeAxialEdgeBend_t</seealso>
public class FeAxialEdgeBend
{
    /// <summary>Edge parameter.</summary>
    public float Te { get; }

    /// <summary>Vertex parameter.</summary>
    public float Tv { get; }

    /// <summary>Rest distance.</summary>
    public float Dist { get; }

    /// <summary>Per-node weights (four entries).</summary>
    public float[] Weight { get; }

    /// <summary>Participating node indices (six entries).</summary>
    public int[] Node { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeAxialEdgeBend"/> class.
    /// </summary>
    /// <param name="data">The axial edge bend key-value object.</param>
    public FeAxialEdgeBend(KVObject data)
    {
        Te = data.GetFloatProperty("te");
        Tv = data.GetFloatProperty("tv");
        Dist = data.GetFloatProperty("flDist");
        Weight = data.GetFloatArray("flWeight");
        Node = data.GetInt32Array("nNode");
    }
}
