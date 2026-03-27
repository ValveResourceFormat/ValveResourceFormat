using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a model skeleton with bones arranged in a hierarchy.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/ModelSkeletonData_t">ModelSkeletonData_t</seealso>
    public class Skeleton
    {
        /// <summary>
        /// Gets the root bones of the skeleton.
        /// </summary>
        public Bone[] Roots { get; private set; } = [];

        /// <summary>
        /// Gets all bones in the skeleton.
        /// </summary>
        public Bone[] Bones { get; private set; } = [];

        /// <summary>
        /// Gets a bone by its StringToken hash.
        /// </summary>
        public Bone? this[uint hash]
        {
            get
            {
                var index = GetBoneIndex(hash);
                return index != -1 ? Bones[index] : null;
            }
        }

        /// <summary>
        /// Gets a bone by its name.
        /// </summary>
        public Bone? this[string name] => this[StringToken.Get(name)];


        /// <summary>
        /// Gets the index of a bone by its StringToken hash, or -1 if not found.
        /// </summary>
        public int GetBoneIndex(uint hash) => boneHashToIndex.TryGetValue(hash, out var index) ? index : -1;

        /// <summary>
        /// Gets the index of a bone by its name, or -1 if not found.
        /// </summary>
        public int GetBoneIndex(string name) => GetBoneIndex(StringToken.Get(name));

        /// <summary>
        /// Gets the root bone for cloth simulation, if present.
        /// </summary>
        public Bone? ClothSimulationRoot { get; private set; }

        /// <summary>
        /// Creates a skeleton from model data.
        /// </summary>
        public static Skeleton FromModelData(KVObject modelData)
        {
            // Check if there is any skeleton data present at all
            if (!modelData.ContainsKey("m_modelSkeleton"))
            {
                Console.WriteLine("No skeleton data found.");
            }

            // Construct the armature from the skeleton KV
            return new Skeleton(modelData.GetSubCollection("m_modelSkeleton"));
        }

        /// <summary>
        /// Creates a skeleton from skeleton-specific data.
        /// </summary>
        public static Skeleton FromSkeletonData(KVObject nmSkeleton)
        {
            var boneNames = nmSkeleton.GetArray<string>("m_boneIDs");
            var boneParents = nmSkeleton.GetIntegerArray("m_parentIndices");
            var boneTransforms = nmSkeleton.GetArray("m_parentSpaceReferencePose");

            var boneCount = boneNames.Length;

            var s = new Skeleton
            {
                Bones = new Bone[boneCount],
            };

            for (var i = 0; i < boneCount; i++)
            {
                var position = new Vector3(
                    boneTransforms[i].GetFloatProperty("0"),
                    boneTransforms[i].GetFloatProperty("1"),
                    boneTransforms[i].GetFloatProperty("2")
                );

                var scale = boneTransforms[i].GetFloatProperty("3");

                var rotation = new Quaternion(
                    boneTransforms[i].GetFloatProperty("4"),
                    boneTransforms[i].GetFloatProperty("5"),
                    boneTransforms[i].GetFloatProperty("6"),
                    boneTransforms[i].GetFloatProperty("7")
                );

                var bone = new Bone(i, boneNames[i], position, rotation, ModelSkeletonBoneFlags.NoBoneFlags);
                s.Bones[i] = bone;
            }

            s.SetBoneParents(boneParents);
            return s;
        }

        readonly Dictionary<uint, int> boneHashToIndex = [];

        private Skeleton()
        {
        }

        /// <summary>
        /// Construct the Armature object from mesh skeleton KV data.
        /// </summary>
        private Skeleton(KVObject skeletonData)
        {
            var boneNames = skeletonData.GetArray<string>("m_boneName");
            var boneParents = skeletonData.GetIntegerArray("m_nParent");
            var boneFlags = skeletonData.GetIntegerArray("m_nFlag")
                .Select(flags => (ModelSkeletonBoneFlags)flags)
                .ToArray();
            var bonePositions = skeletonData.GetArray("m_bonePosParent", v => v.ToVector3());
            var boneRotations = skeletonData.GetArray("m_boneRotParent", v => v.ToQuaternion());

            var boneCount = boneNames.Length;
            Bones = new Bone[boneCount];

            for (var i = 0; i < boneCount; i++)
            {
                var bone = new Bone(i, boneNames[i], bonePositions[i], boneRotations[i], boneFlags[i]);
                Bones[i] = bone;

                if ((bone.Flags & ModelSkeletonBoneFlags.ProceduralCloth) == ModelSkeletonBoneFlags.Cloth
                && ClothSimulationRoot == null)
                {
                    ClothSimulationRoot = bone;
                }
            }

            SetBoneParents(boneParents);
        }

        private void SetBoneParents(long[] boneParents)
        {
            var roots = new List<Bone>();
            foreach (var bone in Bones)
            {
                var parentId = boneParents[bone.Index];
                if (parentId != -1)
                {
                    bone.SetParent(Bones[parentId]);
                    continue;
                }

                roots.Add(bone);
            }

            Roots = [.. roots];

            for (var i = 0; i < Bones.Length; i++)
            {
                var name = Bones[i].Name;
                var hash = StringToken.Store(name);
                boneHashToIndex[hash] = i;
            }
        }
    }
}
