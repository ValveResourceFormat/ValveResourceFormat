using System.Linq;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SoftbodyPhysics;
public class SphereCollectionRigid : SoftbodyCollider
{
    public Vector3[] Center { get; set; }

    public float[] Radius { get; set; }

    public SphereCollectionRigid(KVObject data) : base(data)
    {
        Center = data.GetArray("vSphere").Select(v => v.ToVector3()).ToArray();
        Radius = data.GetArray("vSphere").Select(v => v.GetFloatProperty("3")).ToArray();
    }
}
