using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics
{
    public struct Shape
    {
        public SphereDescriptor[] Spheres { get; set; }
        public CapsuleDescriptor[] Capsules { get; set; }
        public HullDescriptor[] Hulls { get; set; }
        public MeshDescriptor[] Meshes { get; set; }
        public int[] CollisionAttributeIndices { get; set; }

        public Shape(KVObject data)
        {
            Spheres = LoadShapeDescriptorArray<SphereDescriptor, Shapes.Sphere>(data, "m_spheres");
            Capsules = LoadShapeDescriptorArray<CapsuleDescriptor, Shapes.Capsule>(data, "m_capsules");
            Hulls = LoadShapeDescriptorArray<HullDescriptor, Shapes.Hull>(data, "m_hulls");
            Meshes = LoadShapeDescriptorArray<MeshDescriptor, Shapes.Mesh>(data, "m_meshes");
            CollisionAttributeIndices = data.GetArray<object>("m_CollisionAttributeIndices")
                .Select(Convert.ToInt32).ToArray();
        }

        private static TDescriptor[] LoadShapeDescriptorArray<TDescriptor, TShape>(KVObject data, string name)
            where TDescriptor : ShapeDescriptor<TShape>, new()
            where TShape : struct
        {
            var arrayData = data.GetArray(name);
            var array = new TDescriptor[arrayData.Length];
            for (var a = 0; a < arrayData.Length; a++)
            {
                array[a] = new TDescriptor();
                array[a].KV3Transfer(arrayData[a]);
            }

            return array;
        }
    }
}
