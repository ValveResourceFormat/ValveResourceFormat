using System.Linq;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class Skeleton
    {
        public Bone[] Roots { get; private set; }
        public Bone[] Bones { get; private set; }
        public int[] LocalRemapTable { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Skeleton"/> class.
        /// </summary>
        public static Skeleton FromModelData(KVObject modelData, bool filterBonesUsedByLod0)
        {
            // Check if there is any skeleton data present at all
            if (!modelData.ContainsKey("m_modelSkeleton"))
            {
                Console.WriteLine("No skeleton data found.");
            }

            // Construct the armature from the skeleton KV
            return new Skeleton(modelData.GetSubCollection("m_modelSkeleton"), filterBonesUsedByLod0);
        }

        /// <summary>
        /// Construct the Armature object from mesh skeleton KV data.
        /// </summary>
        private Skeleton(KVObject skeletonData, bool filterBonesUsedByLod0)
        {
            var boneNames = skeletonData.GetArray<string>("m_boneName");
            var boneParents = skeletonData.GetIntegerArray("m_nParent");
            var boneFlags = skeletonData.GetIntegerArray("m_nFlag")
                .Select(flags => (ModelSkeletonBoneFlags)flags)
                .ToArray();
            var bonePositions = skeletonData.GetArray("m_bonePosParent", v => v.ToVector3());
            var boneRotations = skeletonData.GetArray("m_boneRotParent", v => v.ToQuaternion());

            var boneIds = Enumerable.Range(0, boneNames.Length);

            if (filterBonesUsedByLod0)
            {
                LocalRemapTable = new int[boneNames.Length];
                var currentRemappedBone = 0;
                for (var i = 0; i < LocalRemapTable.Length; i++)
                {
                    LocalRemapTable[i] = (boneFlags[i] & ModelSkeletonBoneFlags.BoneUsedByVertexLod0) != 0
                        ? currentRemappedBone++
                        : -1;
                }
            }
            else
            {
                LocalRemapTable = boneIds.ToArray();
            }

            // Initialise bone array
            Bones = (filterBonesUsedByLod0 ? boneIds.Where(i => (boneFlags[i] & ModelSkeletonBoneFlags.BoneUsedByVertexLod0) != 0) : boneIds)
                .Select((boneID, i) => new Bone(i, boneNames[boneID], bonePositions[boneID], boneRotations[boneID]))
                .ToArray();

            for (var i = 0; i < LocalRemapTable.Length; i++)
            {
                var remappeBoneID = LocalRemapTable[i];
                if (remappeBoneID != -1 && boneParents[i] != -1)
                {
                    var remappedParent = LocalRemapTable[boneParents[i]];
                    Bones[remappeBoneID].SetParent(Bones[remappedParent]);
                }
            }

            // Create an empty root list
            Roots = Bones.Where(bone => bone.Parent == null).ToArray();
        }
    }
}
