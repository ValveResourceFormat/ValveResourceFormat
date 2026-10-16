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
        /// Gets or sets the compound descriptors.
        /// </summary>
        public CompoundDescriptor[] Compounds { get; set; }
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
            Compounds = LoadShapeDescriptorArray<CompoundDescriptor, Shapes.Compound>(data, "m_compounds");
            CollisionAttributeIndices = data.GetArray<object>("m_CollisionAttributeIndices")!
                .Select(Convert.ToInt32).ToArray();
        }

        /// <summary>Enumerates every sphere, root ones and compound children.</summary>
        public readonly IEnumerable<SphereDescriptor> GetAllSpheres() => Spheres.Concat(Compounds.SelectMany(static c => c.SphereDescriptors));

        /// <summary>Enumerates every capsule, root ones and compound children.</summary>
        public readonly IEnumerable<CapsuleDescriptor> GetAllCapsules() => Capsules.Concat(Compounds.SelectMany(static c => c.CapsuleDescriptors));

        /// <summary>Enumerates every hull, root ones and compound children.</summary>
        public readonly IEnumerable<HullDescriptor> GetAllHulls() => Hulls.Concat(Compounds.SelectMany(static c => c.HullDescriptors));

        /// <summary>Enumerates every mesh, root ones and compound children.</summary>
        public readonly IEnumerable<MeshDescriptor> GetAllMeshes() => Meshes.Concat(Compounds.SelectMany(static c => c.MeshDescriptors));

        private static TDescriptor[] LoadShapeDescriptorArray<TDescriptor, TShape>(KVObject data, string name)
            where TDescriptor : ShapeDescriptor<TShape>, new()
            where TShape : struct
        {
            var arrayData = data.GetArray(name);

            if (arrayData == null) // compounds
            {
                return [];
            }

            var array = new TDescriptor[arrayData.Count];
            for (var a = 0; a < arrayData.Count; a++)
            {
                array[a] = new TDescriptor();
                array[a].KV3Transfer(arrayData[a]);
            }

            return array;
        }
    }
}
