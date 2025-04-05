using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;
public abstract class SoftbodyCollider
{
    public Int32 Node { get; set; }

    public UInt32 Flags { get; set; }

    public UInt32 VertexMapIndex { get; set; }

    public bool[] CollissionMask { get; set; }

    protected SoftbodyCollider(KVObject data)
    {
        Node = data.GetInt32Property("nNode");
        Flags = data.GetUInt32Property("nFlags");
        VertexMapIndex = data.GetUInt32Property("nVertexMapIndex");

        var bitMask = data.GetUInt32Property("nCollisionMask");
        CollissionMask = new bool[4];
        for (var i = 0; i < 4; i++)
        {
            CollissionMask[i] = (bitMask & (1 << i)) != 0;
        }
    }
}
