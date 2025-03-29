using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;
public class BoxRigid : SoftbodyCollider
{
    public Vector3 Origin { get; set; }

    public Vector3 Size { get; set; }

    public Quaternion Orientation { get; set; }

    public BoxRigid(KVObject data) : base(data)
    {
        // layout is origin vec3, 1, orientation quaterion
        var frame = data.GetFloatArray("tmFrame2");
        Origin = new Vector3(frame);
        Size = new Vector3(data.GetFloatArray("vSize"));
        Orientation = new Quaternion(frame[4], frame[5], frame[6], frame[7]);
    }
}
