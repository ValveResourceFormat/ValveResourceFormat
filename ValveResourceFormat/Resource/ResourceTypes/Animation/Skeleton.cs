using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ValveResourceFormat.ResourceTypes.Animation
{
    public class Skeleton
    {
        public List<Bone> Roots { get; private set; }
        public Bone[] Bones { get; private set; }
        public int LastBone { get; private set; }

        public Skeleton()
        {
            Bones = new Bone[0];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Skeleton"/> class.
        /// </summary>
        /// <param name="model"></param>
        public Skeleton(IKeyValueCollection modelData)
        {
            Bones = new Bone[0];
            Roots = new List<Bone>();

            // Check if there is any skeleton data present at all
            if (!modelData.ContainsKey("m_modelSkeleton"))
            {
                Console.WriteLine("No skeleton data found.");
            }

            // Get the remap table and invert it for our construction method
            var remapTable = modelData.GetArray<long>("m_remappingTable");
            var invMapTable = new Dictionary<long, int>();
            for (var i = 0; i < remapTable.Length; i++)
            {
                if (!invMapTable.ContainsKey(remapTable[i]))
                {
                    invMapTable.Add(remapTable[i], i);
                }
            }

            // Construct the armature from the skeleton KV
            ConstructFromNTRO(modelData.GetSubCollection("m_modelSkeleton"), invMapTable);
        }

        /// <summary>
        /// Construct the Armature object from mesh skeleton KV data.
        /// </summary>
        /// <param name="skeletonData"></param>
        /// <param name="remapTable"></param>
        public void ConstructFromNTRO(IKeyValueCollection skeletonData, Dictionary<long, int> remapTable)
        {
            Vector3 MapToVector3(IKeyValueCollection collection) => new Vector3(
                (float)collection.GetProperty<double>("0"),
                (float)collection.GetProperty<double>("1"),
                (float)collection.GetProperty<double>("2"));

            Quaternion MapToQuaternion(IKeyValueCollection collection) => new Quaternion(
                (float)collection.GetProperty<double>("0"),
                (float)collection.GetProperty<double>("1"),
                (float)collection.GetProperty<double>("2"),
                (float)collection.GetProperty<double>("3"));

            var boneNames = skeletonData.GetArray<string>("m_boneName");
            var boneParents = skeletonData.GetArray<long>("m_nParent");
            var bonePositions = skeletonData.GetArray("m_bonePosParent", MapToVector3);
            var boneRotations = skeletonData.GetArray("m_boneRotParent", MapToQuaternion);

            // Initialise bone array
            Bones = new Bone[boneNames.Length];

            //Add all bones to the list
            for (var i = 0; i < boneNames.Length; i++)
            {
                var name = boneNames[i];

                var position = new Vector3(bonePositions[i].X, bonePositions[i].Y, bonePositions[i].Z);
                var rotation = new Quaternion(boneRotations[i].X, boneRotations[i].Y, boneRotations[i].Z, boneRotations[i].W);

                // Create bone
                var index = remapTable.ContainsKey(i) ? remapTable[i] : -1;
                var bone = new Bone(name, index, position, rotation);

                if (boneParents[i] != -1)
                {
                    bone.SetParent(Bones[boneParents[i]]);
                    Bones[boneParents[i]].AddChild(bone);
                }

                Bones[i] = bone;
            }

            FindRoots();

            // Figure out the index of the last bone so we dont have to do that every draw call
            LastBone = Bones.Length > 0 ? Bones.Max(b => b.Index) : -1;
        }

        /// <summary>
        /// Find all skeleton roots (bones without a parent)
        /// </summary>
        private void FindRoots()
        {
            // Create an empty root list
            Roots = new List<Bone>();

            foreach (var bone in Bones)
            {
                if (bone.Parent == null)
                {
                    Roots.Add(bone);
                }
            }
        }
    }
}
