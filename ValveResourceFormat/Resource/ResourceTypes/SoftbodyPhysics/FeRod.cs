using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// A distance constraint (spring) between two nodes.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeRodConstraint_t">FeRodConstraint_t</seealso>
public class FeRod
{
    /// <summary>The two endpoint node indices.</summary>
    public int[] Node { get; }

    /// <summary>Maximum allowed distance between the endpoints.</summary>
    public float MaxDist { get; }

    /// <summary>Minimum allowed distance between the endpoints.</summary>
    public float MinDist { get; }

    /// <summary>Relative weight of the first endpoint when resolving the constraint.</summary>
    public float Weight0 { get; }

    /// <summary>Relaxation factor applied to the constraint.</summary>
    public float RelaxationFactor { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeRod"/> class.
    /// </summary>
    /// <param name="data">The rod constraint key-value object.</param>
    public FeRod(KVObject data)
    {
        Node = data.GetInt32Array("nNode");
        MaxDist = data.GetFloatProperty("flMaxDist");
        MinDist = data.GetFloatProperty("flMinDist");
        Weight0 = data.GetFloatProperty("flWeight0");
        RelaxationFactor = data.GetFloatProperty("flRelaxationFactor");
    }
}
