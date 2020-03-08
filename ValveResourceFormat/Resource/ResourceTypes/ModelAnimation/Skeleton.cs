using System;
using System.Collections.Generic;
using System.Linq;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    public class Skeleton
    {
        private const int BoneUsedByVertexLod0 = 0x00000400;

        public List<Bone> Roots { get; private set; } = new List<Bone>();
        public Bone[] Bones { get; private set; } = Array.Empty<Bone>();
        public int AnimationTextureSize { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Skeleton"/> class.
        /// </summary>
        public Skeleton(IKeyValueCollection modelData)
        {
            // Check if there is any skeleton data present at all
            if (!modelData.ContainsKey("m_modelSkeleton"))
            {
                Console.WriteLine("No skeleton data found.");
            }

            // Get the remap table and invert it for our construction method
            var remapTable = modelData.GetIntegerArray("m_remappingTable");

            var start = 0;
            var end = remapTable.Length;

            var remapTableStarts = modelData.GetIntegerArray("m_remappingTableStarts");

            // we only use lod 1
            if (remapTableStarts.Length > 1)
            {
                start = (int)remapTableStarts[0];
                end = (int)remapTableStarts[1];
            }

            var invMapTable = remapTable.Skip(start).Take(end - start)
                .Select((mapping, index) => (mapping, index))
                .ToLookup(mi => mi.mapping, mi => mi.index);

            AnimationTextureSize = invMapTable.Select(g => g.Max()).Max() + 1;

            // Construct the armature from the skeleton KV
            ConstructFromNTRO(modelData.GetSubCollection("m_modelSkeleton"), invMapTable);
        }

        /// <summary>
        /// Construct the Armature object from mesh skeleton KV data.
        /// </summary>
        private void ConstructFromNTRO(IKeyValueCollection skeletonData, ILookup<long, int> remapTable)
        {
            var boneNames = skeletonData.GetArray<string>("m_boneName");
            var boneParents = skeletonData.GetIntegerArray("m_nParent");
            var boneFlags = skeletonData.GetIntegerArray("m_nFlag");
            var bonePositions = skeletonData.GetArray("m_bonePosParent", v => v.ToVector3());
            var boneRotations = skeletonData.GetArray("m_boneRotParent", v => v.ToQuaternion());

            // Initialise bone array
            Bones = new Bone[boneNames.Length];

            //Add all bones to the list
            for (var i = 0; i < boneNames.Length; i++)
            {
                if ((boneFlags[i] & BoneUsedByVertexLod0) != BoneUsedByVertexLod0)
                {
                    continue;
                }

                var name = boneNames[i];

                var position = bonePositions[i];
                var rotation = boneRotations[i];

                // Create bone
                var bone = new Bone(name, remapTable[i].ToList(), position, rotation);

                if (boneParents[i] != -1)
                {
                    bone.SetParent(Bones[boneParents[i]]);
                    Bones[boneParents[i]].AddChild(bone);
                }

                Bones[i] = bone;
            }

            FindRoots();
        }

        /// <summary>
        /// Find all skeleton roots (bones without a parent).
        /// </summary>
        private void FindRoots()
        {
            // Create an empty root list
            Roots = new List<Bone>();

            foreach (var bone in Bones)
            {
                if (bone != null && bone.Parent == null)
                {
                    Roots.Add(bone);
                }
            }
        }
    }
}
