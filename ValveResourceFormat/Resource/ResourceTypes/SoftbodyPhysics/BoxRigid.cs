using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// A box rigid collider.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/FeBoxRigid_t">FeBoxRigid_t</seealso>
public class BoxRigid : SoftbodyCollider
{
    /// <summary>
    /// Center of the box in the parent node's space (translation part of <c>tmFrame2</c>).
    /// </summary>
    public Vector3 Origin { get; }

    /// <summary>
    /// Half-extents of the box (<c>vSize</c>).
    /// </summary>
    public Vector3 Size { get; }

    /// <summary>
    /// Orientation of the box (rotation part of <c>tmFrame2</c>).
    /// </summary>
    public Quaternion Orientation { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BoxRigid"/> class.
    /// </summary>
    /// <param name="data">The collider key-value object.</param>
    public BoxRigid(KVObject data) : base(data)
    {
        // tmFrame2 is a CTransform stored as [originX, originY, originZ, scale, quatX, quatY, quatZ, quatW]
        var frame = data.GetFloatArray("tmFrame2");
        Origin = new Vector3(frame);
        Size = new Vector3(data.GetFloatArray("vSize"));
        Orientation = new Quaternion(frame[4], frame[5], frame[6], frame[7]);
    }
}
