using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes
{
    public struct Sphere
    {
        public Vector3 Center { get; set; }
        public float Radius { get; set; }

        public Sphere(IKeyValueCollection data)
        {
            Center = data.GetSubCollection("m_vCenter").ToVector3();
            Radius = data.GetFloatProperty("m_flRadius");
        }
    }
}
