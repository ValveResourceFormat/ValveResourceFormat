using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// Friction parameters for a span of nodes colliding with the world.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeWorldCollisionParams_t">FeWorldCollisionParams_t</seealso>
public class FeWorldCollisionParams
{
    /// <summary>Friction against the world.</summary>
    public float WorldFriction { get; }

    /// <summary>Friction against the ground.</summary>
    public float GroundFriction { get; }

    /// <summary>Start index into the world collision node list.</summary>
    public int ListBegin { get; }

    /// <summary>End index into the world collision node list.</summary>
    public int ListEnd { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeWorldCollisionParams"/> class.
    /// </summary>
    /// <param name="data">The world collision params key-value object.</param>
    public FeWorldCollisionParams(KVObject data)
    {
        WorldFriction = data.GetFloatProperty("flWorldFriction");
        GroundFriction = data.GetFloatProperty("flGroundFriction");
        ListBegin = data.GetInt32Property("nListBegin");
        ListEnd = data.GetInt32Property("nListEnd");
    }
}
