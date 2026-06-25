using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// An axis-aligned box that limits how far a node may stray.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeNodeStrayBox_t">FeNodeStrayBox_t</seealso>
public class FeNodeStrayBox
{
    /// <summary>Minimum corner of the box.</summary>
    public Vector3 Min { get; }

    /// <summary>Maximum corner of the box.</summary>
    public Vector3 Max { get; }

    /// <summary>The two node indices this box applies to.</summary>
    public int[] Node { get; }

    /// <summary>Raw flags.</summary>
    public uint Flags { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeNodeStrayBox"/> class.
    /// </summary>
    /// <param name="data">The stray box key-value object.</param>
    public FeNodeStrayBox(KVObject data)
    {
        var min = data.GetFloatArray("vMin");
        var max = data.GetFloatArray("vMax");
        Min = min.Length >= 3 ? new Vector3(min) : Vector3.Zero;
        Max = max.Length >= 3 ? new Vector3(max) : Vector3.Zero;
        Node = data.GetInt32Array("nNode");
        Flags = data.GetUInt32Property("nFlags");
    }
}
