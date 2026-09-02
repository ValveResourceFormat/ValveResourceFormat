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
                .Select(v => v.ToMatrix4x4())
                .ToArray();

        /// <summary>
        /// Gets the physics parts (shapes) in this aggregate.
        /// </summary>
        public Part[] Parts
            => parts ??= Data.GetArray("m_parts").Select(p => new Part(p)).ToArray();

        /// <summary>
        /// Gets the joints (constraints) between parts in this aggregate.
        /// </summary>
        public Joint[] Joints
            => joints ??= Data.GetArray("m_joints")?.Select(j => new Joint(j)).ToArray() ?? [];

        /// <summary>
        /// Gets the referenced bone names.
        /// </summary>
        public string[] BoneNames => Data.GetArray<string>("m_boneNames");

        /// <summary>
        /// Gets the bone parent indices for each part.
        /// </summary>
        public uint[] BoneParents => Data.GetArray<uint>("m_boneParents");

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

        /// <summary>
        /// Gets what a shape with these collision attributes interacts as. Assets compiled before the
        /// rename carry the tags under <c>m_PhysicsTagStrings</c>, and one with neither has no tags.
        /// </summary>
        public static string[] GetInteractAsTags(KVObject collisionAttributes)
        {
            ArgumentNullException.ThrowIfNull(collisionAttributes);

            return collisionAttributes.GetArray<string>("m_InteractAsStrings")
                ?? collisionAttributes.GetArray<string>("m_PhysicsTagStrings")
                ?? [];
        }

        /// <summary>
        /// Gets what a shape interacts as, by index into <see cref="CollisionAttributes"/>. Empty when
        /// the index addresses no attribute set.
        /// </summary>
        public string[] GetInteractAsTags(int collisionAttributeIndex)
            => collisionAttributeIndex >= 0 && collisionAttributeIndex < CollisionAttributes.Count
                ? GetInteractAsTags(CollisionAttributes[collisionAttributeIndex])
                : [];

        private Matrix4x4[]? bindPose;
        private Part[]? parts;
        private Joint[]? joints;
        private uint[]? surfacePropertyHashes;
        private IReadOnlyList<KVObject>? collisionAttributes;

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

        /// <summary>
        /// Gets the parent bone name for a given physics aggregate part.
        /// </summary>
        public string GetParentBoneName(int partIndex)
        {
            var boneParents = BoneParents;
            var boneNames = BoneNames;

            if (boneParents == null || boneNames == null)
            {
                return string.Empty;
            }

            if (partIndex < 0 || partIndex >= boneParents.Length)
            {
                return string.Empty;
            }

            var parentIndex = boneParents[partIndex];
            if (parentIndex < 0 || parentIndex >= boneNames.Length)
            {
                return string.Empty;
            }

            return boneNames[parentIndex];
        }
    }
}
