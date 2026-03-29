using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents physics aggregate data containing collision shapes and properties.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/VPhysXAggregateData_t">VPhysXAggregateData_t</seealso>
    public class PhysAggregateData : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets the physics flags.
        /// </summary>
        public int Flags
            => Data.GetInt32Property("m_nFlags");

        /// <summary>
        /// Gets the bind pose transformation matrices.
        /// </summary>
        public Matrix4x4[] BindPose
           => bindPose ??= Data.GetArray("m_bindPose")
                .Select(v => Matrix4x4FromArray(v.Children.Select(c => c.Value)
                    .Select(m => Convert.ToSingle(m, CultureInfo.InvariantCulture))
                    .ToArray()))
                .ToArray();

        /// <summary>
        /// Gets the physics parts (shapes) in this aggregate.
        /// </summary>
        public Part[] Parts
            => parts ??= Data.GetArray("m_parts").Select(p => new Part(p)).ToArray();

        /// <summary>
        /// Gets the surface property hashes for collision materials.
        /// </summary>
        public uint[] SurfacePropertyHashes
            => surfacePropertyHashes ??= Data.GetArray<object>("m_surfacePropertyHashes").Select(Convert.ToUInt32).ToArray();

        /// <summary>
        /// Gets the collision attributes.
        /// </summary>
        public IReadOnlyList<KVObject> CollisionAttributes
            => collisionAttributes ??= Data.GetArray("m_collisionAttributes");

        private Matrix4x4[]? bindPose;
        private Part[]? parts;
        private uint[]? surfacePropertyHashes;
        private KVObject[]? collisionAttributes;

        /// <summary>
        /// Initializes a new instance of the <see cref="PhysAggregateData"/> class.
        /// </summary>
        public PhysAggregateData()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PhysAggregateData"/> class.
        /// </summary>
        public PhysAggregateData(BlockType type) : base(type, "VPhysXAggregateData_t")
        {
        }

        static Matrix4x4 Matrix4x4FromArray(float[] a)
            => new(a[0], a[4], a[8], 0,
                   a[1], a[5], a[9], 0,
                   a[2], a[6], a[10], 0,
                   a[3], a[7], a[11], 1);
    }
}
