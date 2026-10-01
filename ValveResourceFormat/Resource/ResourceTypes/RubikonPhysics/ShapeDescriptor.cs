using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics
{
    /// <summary>
    /// Base descriptor for physics shapes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnShapeDesc_t">RnShapeDesc_t</seealso>
    public class ShapeDescriptor<T> where T : struct
    {
        /// <summary>
        /// Gets or sets the collision attribute index.
        /// </summary>
        public int CollisionAttributeIndex { get; set; }
        /// <summary>
        /// Gets or sets the surface property index.
        /// </summary>
        public int SurfacePropertyIndex { get; set; }
        /// <summary>
        /// Gets or sets the user-friendly name.
        /// </summary>
        public string? UserFriendlyName { get; set; }
        /// <summary>
        /// Gets or sets the hit group this shape belongs to for location based damage.
        /// </summary>
        public string? HitGroupName { get; set; }

        /// <summary>
        /// Gets or sets the shape.
        /// </summary>
        public T Shape { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeDescriptor{T}"/> class.
        /// </summary>
        protected ShapeDescriptor()
        {
        }

        /// <summary>
        /// Transfers data from a KVObject.
        /// </summary>
        public void KV3Transfer(KVObject data)
        {
            CollisionAttributeIndex = data.GetInt32Property("m_nCollisionAttributeIndex");
            SurfacePropertyIndex = data.GetInt32Property("m_nSurfacePropertyIndex");
            UserFriendlyName = data.GetStringProperty("m_UserFriendlyName");
            HitGroupName = data.GetStringProperty("m_sHitGroupName");

            var memberName = typeof(T).Name;
            var shapeData = data.GetSubCollection("m_" + memberName) ?? throw new InvalidDataException("Member name is not correct for shape type: " + memberName);
            Shape = DeserializeShape(shapeData);
        }

        /// <summary>
        /// Deserializes the shape from a KVObject.
        /// </summary>
        public virtual T DeserializeShape(KVObject data)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Descriptor for sphere shapes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnSphereDesc_t">RnSphereDesc_t</seealso>
    public class SphereDescriptor : ShapeDescriptor<Sphere>
    {
        /// <inheritdoc/>
        public override Sphere DeserializeShape(KVObject data) => new(data);
    }

    /// <summary>
    /// Descriptor for capsule shapes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnCapsuleDesc_t">RnCapsuleDesc_t</seealso>
    public class CapsuleDescriptor : ShapeDescriptor<Capsule>
    {
        /// <inheritdoc/>
        public override Capsule DeserializeShape(KVObject data) => new(data);
    }

    /// <summary>
    /// Descriptor for hull shapes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnHullDesc_t">RnHullDesc_t</seealso>
    public class HullDescriptor : ShapeDescriptor<Hull>
    {
        /// <inheritdoc/>
        public override Hull DeserializeShape(KVObject data) => new(data);
    }

    /// <summary>
    /// Descriptor for mesh shapes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnMeshDesc_t">RnMeshDesc_t</seealso>
    public class MeshDescriptor : ShapeDescriptor<Shapes.Mesh>
    {
        /// <inheritdoc/>
        public override Shapes.Mesh DeserializeShape(KVObject data) => new(data);
    }

    /// <summary>
    /// Descriptor for compound shapes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnCompoundDesc_t">RnCompoundDesc_t</seealso>
    public class CompoundDescriptor : ShapeDescriptor<Compound>
    {
        private SphereDescriptor[]? sphereDescriptors;
        private CapsuleDescriptor[]? capsuleDescriptors;
        private HullDescriptor[]? hullDescriptors;
        private MeshDescriptor[]? meshDescriptors;

        /// <inheritdoc/>
        public override Compound DeserializeShape(KVObject data) => new(data);

        // Fabricated descriptors
        internal SphereDescriptor[] SphereDescriptors => sphereDescriptors ??= WrapChildren<SphereDescriptor, Sphere>(Shape.Spheres);
        internal CapsuleDescriptor[] CapsuleDescriptors => capsuleDescriptors ??= WrapChildren<CapsuleDescriptor, Capsule>(Shape.Capsules);
        internal HullDescriptor[] HullDescriptors => hullDescriptors ??= WrapChildren<HullDescriptor, Hull>(Shape.Hulls);
        internal MeshDescriptor[] MeshDescriptors => meshDescriptors ??= WrapChildren<MeshDescriptor, Shapes.Mesh>(Shape.Meshes);

        private TDescriptor[] WrapChildren<TDescriptor, TShape>(TShape[] children)
            where TDescriptor : ShapeDescriptor<TShape>, new()
            where TShape : struct
            => [.. children.Select(child => new TDescriptor
            {
                CollisionAttributeIndex = CollisionAttributeIndex,
                SurfacePropertyIndex = SurfacePropertyIndex,
                UserFriendlyName = UserFriendlyName,
                HitGroupName = HitGroupName,
                Shape = child,
            })];
    }
}
