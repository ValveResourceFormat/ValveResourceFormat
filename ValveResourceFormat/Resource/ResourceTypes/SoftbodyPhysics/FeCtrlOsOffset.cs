using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// Object-space parent/child control relationship.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeCtrlOsOffset_t">FeCtrlOsOffset_t</seealso>
public class FeCtrlOsOffset
{
    /// <summary>Index of the parent control.</summary>
    public int CtrlParent { get; }

    /// <summary>Index of the child control.</summary>
    public int CtrlChild { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeCtrlOsOffset"/> class.
    /// </summary>
    /// <param name="data">The control offset key-value object.</param>
    public FeCtrlOsOffset(KVObject data)
    {
        CtrlParent = data.GetInt32Property("nCtrlParent");
        CtrlChild = data.GetInt32Property("nCtrlChild");
    }
}
