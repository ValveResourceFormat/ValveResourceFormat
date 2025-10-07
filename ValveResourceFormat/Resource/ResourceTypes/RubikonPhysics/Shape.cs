using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics
{
    /// <summary>
    /// Represents a physics shape.
    /// </summary>
    public struct Shape
    {
        /// <summary>
        /// Gets or sets the sphere descriptors.
        /// </summary>
        public SphereDescriptor[] Spheres { get; set; }
        /// <summary>
        /// Gets or sets the capsule descriptors.
        /// </summary>
        public CapsuleDescriptor[] Capsules { get; set; }
        /// <summary>
        /// Gets or sets the hull descriptors.
        /// </summary>
        public HullDescriptor[] Hulls { get; set; }
        /// <summary>
        /// Gets or sets the mesh descriptors.
        /// </summary>
        public MeshDescriptor[] Meshes { get; set; }
        /// <summary>
        /// Gets or sets the collision attribute indices.
        /// </summary>
        public int[] CollisionAttributeIndices { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Shape"/> struct.
        /// </summary>
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
