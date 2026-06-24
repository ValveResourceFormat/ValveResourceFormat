using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// A single-sphere rigid collider.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeSphereRigid_t">FeSphereRigid_t</seealso>
public class SphereRigid : SoftbodyCollider
{
    /// <summary>
    /// The sphere stored as a packed <c>fltx4</c>: <c>(X, Y, Z)</c> is the center and <c>W</c> is the radius.
    /// </summary>
    public Vector4 Sphere { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SphereRigid"/> class.
    /// </summary>
    /// <param name="data">The collider key-value object.</param>
    public SphereRigid(KVObject data) : base(data)
    {
        Sphere = new Vector4(data.GetFloatArray("vSphere"));
    }
}
