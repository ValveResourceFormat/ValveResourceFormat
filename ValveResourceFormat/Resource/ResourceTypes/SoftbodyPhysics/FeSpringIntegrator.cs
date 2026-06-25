using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// A spring between two nodes with explicit constants.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeSpringIntegrator_t">FeSpringIntegrator_t</seealso>
public class FeSpringIntegrator
{
    /// <summary>The two endpoint node indices.</summary>
    public int[] Node { get; }

    /// <summary>Rest length of the spring.</summary>
    public float SpringRestLength { get; }

    /// <summary>Spring stiffness constant.</summary>
    public float SpringConstant { get; }

    /// <summary>Spring damping constant.</summary>
    public float SpringDamping { get; }

    /// <summary>Relative weight of the first endpoint.</summary>
    public float NodeWeight0 { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FeSpringIntegrator"/> class.
    /// </summary>
    /// <param name="data">The spring integrator key-value object.</param>
    public FeSpringIntegrator(KVObject data)
    {
        Node = data.GetInt32Array("nNode");
        SpringRestLength = data.GetFloatProperty("flSpringRestLength");
        SpringConstant = data.GetFloatProperty("flSpringConstant");
        SpringDamping = data.GetFloatProperty("flSpringDamping");
        NodeWeight0 = data.GetFloatProperty("flNodeWeight0");
    }
}
