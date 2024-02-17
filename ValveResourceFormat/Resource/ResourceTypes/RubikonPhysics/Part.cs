using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics
{
    public struct Part
    {
        public int Flags { get; set; }
        public float Mass { get; set; }
        public Shape Shape { get; set; }
        public int CollisionAttributeIndex { get; set; }

        public Part(KVObject data)
        {
            Flags = data.GetInt32Property("m_nFlags");
            Mass = data.GetFloatProperty("m_flMass");
            Shape = new Shape(data.GetSubCollection("m_rnShape"));
            CollisionAttributeIndex = data.GetInt32Property("m_nCollisionAttributeIndex");
        }
    }
}
