using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// Basis frame for a procedurally generated cloth node.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeNodeBase_t">FeNodeBase_t</seealso>
public class FeNodeBase
{
    /// <summary>The node this basis belongs to.</summary>
    public int Node { get; }

    /// <summary>First node defining the local X axis.</summary>
    public int NodeX0 { get; }

    /// <summary>Second node defining the local X axis.</summary>
    public int NodeX1 { get; }

    /// <summary>First node defining the local Y axis.</summary>
    public int NodeY0 { get; }

    /// <summary>Second node defining the local Y axis.</summary>
    public int NodeY1 { get; }

    /// <summary>Rotation adjustment applied to the derived basis.</summary>
    public Quaternion QAdjust { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeNodeBase"/> class.
    /// </summary>
    /// <param name="data">The node basis key-value object.</param>
    public FeNodeBase(KVObject data)
    {
        Node = data.GetInt32Property("nNode");
        NodeX0 = data.GetInt32Property("nNodeX0");
        NodeX1 = data.GetInt32Property("nNodeX1");
        NodeY0 = data.GetInt32Property("nNodeY0");
        NodeY1 = data.GetInt32Property("nNodeY1");

        var q = data.GetFloatArray("qAdjust");
        QAdjust = q.Length >= 4 ? new Quaternion(q[0], q[1], q[2], q[3]) : Quaternion.Identity;
    }
}
