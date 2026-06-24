using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

/// <summary>
/// Base class for the rigid colliders that a softbody (cloth) simulation collides against.
/// </summary>
public abstract class SoftbodyCollider
{
    /// <summary>
    /// Index of the parent node (into <see cref="PhysFeModel.CtrlName"/>) this collider is attached to.
    /// </summary>
    public int Node { get; }

    /// <summary>
    /// Raw collider flags (<c>nFlags</c>).
    /// </summary>
    public uint Flags { get; }

    /// <summary>
    /// Index of the vertex map (<c>nVertexMapIndex</c>) restricting which nodes collide.
    /// </summary>
    public uint VertexMapIndex { get; }

    /// <summary>
    /// The four collision layers enabled for this collider, decoded from the <c>nCollisionMask</c> bitmask.
    /// </summary>
    public bool[] CollisionMask { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftbodyCollider"/> class from the shared collider fields.
    /// </summary>
    /// <param name="data">The collider key-value object.</param>
    protected SoftbodyCollider(KVObject data)
    {
        Node = data.GetInt32Property("nNode");
        Flags = data.GetUInt32Property("nFlags");
        VertexMapIndex = data.GetUInt32Property("nVertexMapIndex");

        var bitMask = data.GetUInt32Property("nCollisionMask");
        CollisionMask = new bool[4];
        for (var i = 0; i < 4; i++)
        {
            CollisionMask[i] = (bitMask & (1u << i)) != 0;
        }
    }
}
