using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// A tapered-capsule rigid collider: two spheres swept into a capsule.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeTaperedCapsuleRigid_t">FeTaperedCapsuleRigid_t</seealso>
public class TaperedCapsuleRigid : SoftbodyCollider
{
    /// <summary>
    /// The two cap spheres, each stored as a packed <c>fltx4</c>: <c>(X, Y, Z)</c> is the center and <c>W</c> is the radius.
    /// </summary>
    public Vector4[] Spheres { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaperedCapsuleRigid"/> class.
    /// </summary>
    /// <param name="data">The collider key-value object.</param>
    public TaperedCapsuleRigid(KVObject data) : base(data)
    {
        Spheres = data.GetArray("vSphere").Select(v => v.ToVector4()).ToArray();
    }
}
