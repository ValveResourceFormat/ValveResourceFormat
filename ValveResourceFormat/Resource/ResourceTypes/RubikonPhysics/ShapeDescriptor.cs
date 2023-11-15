using System;
using System.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics
{
    public class ShapeDescriptor<T> where T : struct
    {
        public int CollisionAttributeIndex { get; set; }
        public int SurfacePropertyIndex { get; set; }
        public string UserFriendlyName { get; set; }

        public T Shape { get; set; }

        protected ShapeDescriptor()
        {
        }

        public void KV3Transfer(IKeyValueCollection data)
        {
            CollisionAttributeIndex = data.GetInt32Property("m_nCollisionAttributeIndex");
            SurfacePropertyIndex = data.GetInt32Property("m_nSurfacePropertyIndex");
            UserFriendlyName = data.GetStringProperty("m_UserFriendlyName");

            var memberName = typeof(T).Name;
            var shapeData = data.GetSubCollection("m_" + memberName) ?? throw new InvalidDataException("Member name is not correct for shape type: " + memberName);
            Shape = DeserializeShape(shapeData);
        }

        public virtual T DeserializeShape(IKeyValueCollection data)
        {
            throw new NotImplementedException();
        }
    }

    public class SphereDescriptor : ShapeDescriptor<Sphere>
    {
        public override Sphere DeserializeShape(IKeyValueCollection data) => new(data);
    }

    public class CapsuleDescriptor : ShapeDescriptor<Capsule>
    {
        public override Capsule DeserializeShape(IKeyValueCollection data) => new(data);
    }

    public class HullDescriptor : ShapeDescriptor<Hull>
    {
        public override Hull DeserializeShape(IKeyValueCollection data) => new(data);
    }

    public class MeshDescriptor : ShapeDescriptor<Shapes.Mesh>
    {
        public override Shapes.Mesh DeserializeShape(IKeyValueCollection data) => new(data);
    }
}
