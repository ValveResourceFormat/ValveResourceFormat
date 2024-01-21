using System.Linq;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes
{
    public struct Capsule
    {
        public Vector3[] Center { get; set; }
        public float Radius { get; set; }

        public Capsule(IKeyValueCollection data)
        {
            Center = data.GetArray("m_vCenter").Select(v => v.ToVector3()).ToArray();
            Radius = data.GetFloatProperty("m_flRadius");
        }
    }
}
