using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics
{
    /// <summary>
    /// Represents a physics shape.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/VPhysics2ShapeDef_t">VPhysics2ShapeDef_t</seealso>
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
            CollisionAttributeIndices = data.GetArray<object>("m_CollisionAttributeIndices")!
                .Select(Convert.ToInt32).ToArray();
        }

        private static TDescriptor[] LoadShapeDescriptorArray<TDescriptor, TShape>(KVObject data, string name)
            where TDescriptor : ShapeDescriptor<TShape>, new()
            where TShape : struct
        {
            var arrayData = data.GetArray(name);
            var descriptors = new List<TDescriptor>(arrayData.Count);
            foreach (var descriptorData in arrayData)
            {
                var descriptor = new TDescriptor();
                descriptor.KV3Transfer(descriptorData);
                descriptors.Add(descriptor);
            }

            var compoundArrayName = "m_" + char.ToUpperInvariant(name[2]) + name[3..];
            foreach (var compoundDescriptor in data.GetArray("m_compounds") ?? [])
            {
                var compound = compoundDescriptor.GetSubCollection("m_Compound")
                    ?? throw new InvalidDataException("Compound descriptor has no m_Compound member.");
                foreach (var child in compound.GetArray(compoundArrayName) ?? [])
                {
                    // Children are bare shapes in part space, not descriptors or BVH-local transforms.
                    var descriptor = new TDescriptor
                    {
                        CollisionAttributeIndex = compoundDescriptor.GetInt32Property("m_nCollisionAttributeIndex"),
                        SurfacePropertyIndex = compoundDescriptor.GetInt32Property("m_nSurfacePropertyIndex"),
                        UserFriendlyName = compoundDescriptor.GetStringProperty("m_UserFriendlyName"),
                        HitGroupName = compoundDescriptor.GetStringProperty("m_sHitGroupName"),
                    };
                    descriptor.Shape = descriptor.DeserializeShape(child);
                    descriptors.Add(descriptor);
                }
            }

            return [.. descriptors];
        }
    }
}
