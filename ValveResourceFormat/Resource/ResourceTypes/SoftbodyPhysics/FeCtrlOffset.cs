using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// Positional offset of a child control relative to its parent.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeCtrlOffset_t">FeCtrlOffset_t</seealso>
public class FeCtrlOffset
{
    /// <summary>Offset of the child control from the parent.</summary>
    public Vector3 Offset { get; }

    /// <summary>Index of the parent control.</summary>
    public int CtrlParent { get; }

    /// <summary>Index of the child control.</summary>
    public int CtrlChild { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeCtrlOffset"/> class.
    /// </summary>
    /// <param name="data">The control offset key-value object.</param>
    public FeCtrlOffset(KVObject data)
    {
        var offset = data.GetFloatArray("vOffset");
        Offset = offset.Length >= 3 ? new Vector3(offset) : Vector3.Zero;
        CtrlParent = data.GetInt32Property("nCtrlParent");
        CtrlChild = data.GetInt32Property("nCtrlChild");
    }
}
